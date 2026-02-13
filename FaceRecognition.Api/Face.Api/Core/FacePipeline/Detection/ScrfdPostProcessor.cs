using Face.Api.Core.FacePipeline.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Drawing;

namespace Face.Api.Core.FacePipeline.Detection
{
    public static class ScrfdPostProcessor
    {   

        private const float ScoreThreshold = 0.6f;
        private const float NmsThreshold = 0.4f;
        private const int InputSize = 640;

        private static readonly int[] Strides = { 8, 16, 32 };

        public static FaceDetection Parse(
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs,
            ScrfdImageInfo info,
            int originalWidth,
            int originalHeight)
        {
            var detections = new List<FaceDetection>();

            foreach (int stride in Strides)
            {
                var scores = outputs.First(o => o.Name == $"score_{stride}")
                                    .AsTensor<float>();

                var boxes = outputs.First(o => o.Name == $"bbox_{stride}")
                                   .AsTensor<float>();

                var kps = outputs.First(o => o.Name == $"kps_{stride}")
                                 .AsTensor<float>();

                DecodeStride(
                    scores,
                    boxes,
                    kps,
                    stride,
                    info,
                    originalWidth,
                    originalHeight,
                    detections
                );

            }

            if (detections.Count == 0)
                throw new Exception("No face detected");

            var finalDetections = ApplyNms(detections);

            return finalDetections
                .OrderByDescending(d => d.Score)
                .First();
        }

        // ======================================================
        // DECODIFICACIÓN SCRFD (ANCHORS PLANOS)
        // ======================================================
        /*
        private static void DecodeStride(
            Tensor<float> scores,
            Tensor<float> boxes,
            Tensor<float> kps,
            int stride,
            ScrfdImageInfo info,
            int imgW,
            int imgH,
            List<FaceDetection> detections)
        {
            // 1. Calcular dimensiones reales basadas en el InputSize (640)
            int featW = InputSize / stride;
            int featH = InputSize / stride;
            int numAnchors = scores.Dimensions[1];

            // Detectar si el modelo tiene 1 o 2 anchors por celda
            int anchorsPerCell = numAnchors / (featW * featH);

            for (int i = 0; i < numAnchors; i++)
            {
                float score = scores[0, i, 0];
                if (score < ScoreThreshold) continue;

                // 2. Calcular posición en el grid correctamente
                int index = i / anchorsPerCell;
                int gridX = index % featW;
                int gridY = index / featW;

                // 3. Punto de anclaje (Centro de la celda del modelo 640x640)
                float cx = (gridX) * stride;
                float cy = (gridY) * stride;

                // 4. Decodificar Bounding Box (Distancias l, t, r, b)
                float x1 = (cx - (boxes[0, i, 0] * stride) - info.PadX) / info.Scale;
                float y1 = (cy - (boxes[0, i, 1] * stride) - info.PadY) / info.Scale;
                float x2 = (cx + (boxes[0, i, 2] * stride) - info.PadX) / info.Scale;
                float y2 = (cy + (boxes[0, i, 3] * stride) - info.PadY) / info.Scale;

                // 5. Decodificar Landmarks (5 puntos)
                var landmarks = new PointF[5];
                for (int p = 0; p < 5; p++)
                {
                    float lx = (cx + (kps[0, i, p * 2] * stride) - info.PadX) / info.Scale;
                    float ly = (cy + (kps[0, i, p * 2 + 1] * stride) - info.PadY) / info.Scale;

                    landmarks[p] = new PointF(
                        Math.Clamp(lx, 0, imgW - 1),
                        Math.Clamp(ly, 0, imgH - 1)
                    );
                }

                // 6. Crear rectángulo asegurando valores positivos
                float left = Math.Max(0, Math.Min(x1, x2));
                float top = Math.Max(0, Math.Min(y1, y2));
                float width = Math.Abs(x2 - x1);
                float height = Math.Abs(y2 - y1);

                detections.Add(new FaceDetection
                {
                    BoundingBox = new Rectangle((int)left, (int)top, (int)width, (int)height),
                    Score = score,
                    Landmarks = landmarks
                });
            }
        } */
        private static void DecodeStride(
            Tensor<float> scores,
            Tensor<float> boxes,
            Tensor<float> kps,
            int stride,
            ScrfdImageInfo info,
            int imgW,
            int imgH,
            List<FaceDetection> detections)
        {
            int featW = InputSize / stride;
            int featH = InputSize / stride;

            int numAnchors = scores.Dimensions[1];

            for (int i = 0; i < numAnchors; i++)
            {
                float score = scores[0, i, 0];
                if (score < ScoreThreshold)
                    continue;

                // 🔥 SCRFD FLATTENED GRID (2 anchors por celda)
                int cellIndex = i >> 1; // i / 2
                int gridX = cellIndex % featW;
                int gridY = cellIndex / featW;

                // 🔥 CENTRO CORRECTO
                float cx = (gridX) * stride;
                float cy = (gridY) * stride;

                // bbox offsets
                float l = boxes[0, i, 0] * stride;
                float t = boxes[0, i, 1] * stride;
                float r = boxes[0, i, 2] * stride;
                float b = boxes[0, i, 3] * stride;

                float x1 = (cx - l - info.PadX) / info.Scale;
                float y1 = (cy - t - info.PadY) / info.Scale;
                float x2 = (cx + r - info.PadX) / info.Scale;
                float y2 = (cy + b - info.PadY) / info.Scale;

                float left = Math.Clamp(Math.Min(x1, x2), 0, imgW - 1);
                float top = Math.Clamp(Math.Min(y1, y2), 0, imgH - 1);
                float right = Math.Clamp(Math.Max(x1, x2), 0, imgW - 1);
                float bottom = Math.Clamp(Math.Max(y1, y2), 0, imgH - 1);

                if (right - left < 4 || bottom - top < 4)
                    continue;

                var rect = new Rectangle(
                    (int)left,
                    (int)top,
                    (int)(right - left),
                    (int)(bottom - top)
                );

                // 🔥 LANDMARKS
                var landmarks = new PointF[5];
                for (int p = 0; p < 5; p++)
                {
                    float lx = (cx + kps[0, i, p * 2] * stride - info.PadX) / info.Scale;
                    float ly = (cy + kps[0, i, p * 2 + 1] * stride - info.PadY) / info.Scale;

                    landmarks[p] = new PointF(
                        Math.Clamp(lx, 0, imgW - 1),
                        Math.Clamp(ly, 0, imgH - 1)
                    );
                }

                detections.Add(new FaceDetection
                {
                    BoundingBox = rect,
                    Score = score,
                    Landmarks = landmarks
                });
            }
        }




        // ======================================================
        // NMS
        // ======================================================

        private static List<FaceDetection> ApplyNms(List<FaceDetection> detections)
        {
            var result = new List<FaceDetection>();

            var sorted = detections
                .OrderByDescending(d => d.Score)
                .ToList();

            while (sorted.Count > 0)
            {
                var best = sorted[0];
                result.Add(best);
                sorted.RemoveAt(0);

                sorted.RemoveAll(d =>
                    IoU(best.BoundingBox, d.BoundingBox) > NmsThreshold);
            }

            return result;
        }

        private static float IoU(Rectangle a, Rectangle b)
        {
            int interX1 = Math.Max(a.Left, b.Left);
            int interY1 = Math.Max(a.Top, b.Top);
            int interX2 = Math.Min(a.Right, b.Right);
            int interY2 = Math.Min(a.Bottom, b.Bottom);

            int interW = Math.Max(0, interX2 - interX1);
            int interH = Math.Max(0, interY2 - interY1);

            float interArea = interW * interH;
            float unionArea = a.Width * a.Height + b.Width * b.Height - interArea;

            return unionArea <= 0 ? 0 : interArea / unionArea;
        }

        private static int Clamp(float value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return (int)value;
        }
    }
}
