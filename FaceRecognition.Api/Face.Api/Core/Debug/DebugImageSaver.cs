using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Face.Api.Core.Debug
{
    public static class DebugImageSaver
    {
        public static void Save(Image<Rgb24> img, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            img.Save(path);
        }
    }
}
