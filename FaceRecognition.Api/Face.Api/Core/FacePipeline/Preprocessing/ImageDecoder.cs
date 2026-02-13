using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Face.Api.Core.FacePipeline.Preprocessing
{
    public static class ImageDecoder
    {
        public static Image<Rgb24> Decode(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                throw new ArgumentException("Image bytes are empty");

            return Image.Load<Rgb24>(imageBytes);
        }
    }
}
