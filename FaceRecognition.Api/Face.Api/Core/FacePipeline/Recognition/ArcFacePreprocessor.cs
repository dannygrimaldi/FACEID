using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Face.Api.Core.FacePipeline.Recognition
{
    public static class ArcFacePreprocessor
    {
        public static DenseTensor<float> ToTensor(Image<Rgb24> aligned)
        {
            const int size = 112;
            var tensor = new DenseTensor<float>(new[] { 1, 3, size, size });

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = aligned[x, y];

                    tensor[0, 0, y, x] = (p.R - 127.5f) / 128f;
                    tensor[0, 1, y, x] = (p.G - 127.5f) / 128f;
                    tensor[0, 2, y, x] = (p.B - 127.5f) / 128f;
                }
            }

            return tensor;
        }
    }
}
