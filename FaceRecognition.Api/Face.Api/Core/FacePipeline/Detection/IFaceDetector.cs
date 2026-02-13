using Face.Api.Core.FacePipeline.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Face.Api.Core.FacePipeline.Detection
{
    public interface IFaceDetector
    {
        FaceDetection Detect(Image<Rgb24> image);
    }
}
