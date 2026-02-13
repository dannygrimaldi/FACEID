using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;


public static class ScrfdPreprocessor
{
    private const int InputSize = 640;

    public static ScrfdImageInfo ToTensor(Image<Rgb24> image)
    {
        int w = image.Width;
        int h = image.Height;

        float scale = Math.Min(
            (float)InputSize / w,
            (float)InputSize / h
        );

        int newW = (int)(w * scale);
        int newH = (int)(h * scale);

        int padX = (InputSize - newW) / 2;
        int padY = (InputSize - newH) / 2;

        using var resized = image.Clone(ctx =>
            ctx.Resize(newW, newH)
        );

        using var canvas = new Image<Rgb24>(InputSize, InputSize);
        canvas.Mutate(ctx => ctx.DrawImage(resized, new Point(padX, padY), 1f));

        var tensor = new DenseTensor<float>(new[] { 1, 3, InputSize, InputSize });

        for (int y = 0; y < InputSize; y++)
        {
            for (int x = 0; x < InputSize; x++)
            {
                var p = canvas[x, y];
                tensor[0, 0, y, x] = (p.R - 127.5f) / 128f;
                tensor[0, 1, y, x] = (p.G - 127.5f) / 128f;
                tensor[0, 2, y, x] = (p.B - 127.5f) / 128f;
            }
        }

        return new ScrfdImageInfo
        {
            Tensor = tensor,
            Scale = scale,
            PadX = padX,
            PadY = padY
        };
    }
}
