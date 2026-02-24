using System.Drawing;
using System.Linq;

namespace Face.Api.Core.FacePipeline.Alignment
{
    public static class FaceGeometry
    {

        public static Rectangle ComputeFaceRectFromLandmarks(
            PointF[] lm,
            int imgW,
            int imgH)
        {
            float minX = lm.Min(p => p.X);
            float maxX = lm.Max(p => p.X);
            float minY = lm.Min(p => p.Y);
            float maxY = lm.Max(p => p.Y);

            float cx = (minX + maxX) / 2f;
            float cy = (minY + maxY) / 2f;

            float size = Math.Max(maxX - minX, maxY - minY);

            // 🔥 margen ArcFace óptimo
            size *= 2.6f;

            // 🔥 pequeño offset vertical (ArcFace bias)
            cy += size * 0.05f;

            float x = cx - size / 2;
            float y = cy - size / 2;

            x = Math.Clamp(x, 0, imgW - 1);
            y = Math.Clamp(y, 0, imgH - 1);

            size = Math.Min(size, Math.Min(imgW - x, imgH - y));

            return new Rectangle(
                (int)MathF.Round(x),
                (int)MathF.Round(y),
                (int)MathF.Round(size),
                (int)MathF.Round(size)
            );
        }


    }
}
