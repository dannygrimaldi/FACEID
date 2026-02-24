using OpenCvSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Runtime.InteropServices;
using System.Linq;

// Alias para evitar ambigüedad con System.Drawing
using ISImage = SixLabors.ImageSharp.Image;

namespace Face.Api.Core.FacePipeline.Alignment
{
    public static class FaceAligner
    {
        private static readonly Point2f[] ArcFaceTemplate =
        {
            new(38.2946f, 51.6963f),
            new(73.5318f, 51.5014f),
            new(56.0252f, 71.7366f),
            new(41.5493f, 92.3655f),
            new(70.7299f, 92.2041f)
        };

        public static Image<Rgb24> Align(Image<Rgb24> image, System.Drawing.PointF[] landmarks)
        {
            if (landmarks.Length != 5)
                throw new ArgumentException("FaceAligner requiere exactamente 5 landmarks");

            var srcPoints = landmarks.Select(p => new Point2f(p.X, p.Y)).ToArray();

            // 1. ImageSharp → Mat
            byte[] pixels = new byte[image.Width * image.Height * 3];
            image.CopyPixelDataTo(pixels);

            using var mat = new Mat(image.Height, image.Width, MatType.CV_8UC3);
            Marshal.Copy(pixels, 0, mat.Data, pixels.Length);
            //using var mat = new Mat(image.Height, image.Width, MatType.CV_8UC3, pixels);


            // *** CORRECCIÓN 1: Convertir inmediatamente a BGR para que OpenCV sea feliz ***
            Cv2.CvtColor(mat, mat, ColorConversionCodes.RGB2BGR);
            // 2. Estimar transformación (Similarity Transform)
            using var M = Cv2.EstimateAffinePartial2D(
                InputArray.Create(srcPoints),
                InputArray.Create(ArcFaceTemplate)
            );

            if (M == null || M.Empty())
                throw new Exception("No se pudo calcular la matriz de alineación.");

            // 3. Aplicar Warp (Resultado 112x112)
            using var alignedMat = new Mat();
            Cv2.WarpAffine(
                mat,
                alignedMat,
                M,
                new OpenCvSharp.Size(112, 112),
                InterpolationFlags.Cubic,
                BorderTypes.Constant,
                new Scalar(0, 0, 0)
            );

            Cv2.CvtColor(alignedMat, alignedMat, ColorConversionCodes.BGR2RGB);
            // 4. Mat → ImageSharp
            byte[] outPixels = new byte[112 * 112 * 3];
            Marshal.Copy(alignedMat.Data, outPixels, 0, outPixels.Length);
            var alignedImage = ISImage.LoadPixelData<Rgb24>(outPixels, 112, 112);

            // 📂 5. GUARDADO AUTOMÁTICO EN DISCO (DEBUG)
            try
            {
                string debugPath = @"C:\temp\face_debug";
                if (!Directory.Exists(debugPath))
                {
                    Directory.CreateDirectory(debugPath);
                }

                // Usamos un timestamp preciso para no sobrescribir archivos
                string fileName = $"face_{DateTime.Now:yyyyMMdd_HHmmss_fff}.jpg";
                string fullPath = Path.Combine(debugPath, fileName);

                alignedImage.SaveAsJpeg(fullPath);
            }
            catch (Exception ex)
            {
                // Loguear error de escritura si es necesario, pero no detener el pipeline
                Console.WriteLine($"Error guardando debug: {ex.Message}");
            }

            return alignedImage;
        }
    }
}