using Face.Api.Core.FacePipeline.Detection;
using Face.Api.Core.FacePipeline.Preprocessing;

namespace Face.Api.Core.FacePipeline
{
    public class FacePipeline
    {
        private readonly IFaceDetector _detector;

        public FacePipeline(IFaceDetector detector)
        {
            _detector = detector;
        }

        public float[] Process(byte[] imageBytes)
        {
            var image = ImageDecoder.Decode(imageBytes);

            var detection = _detector.Detect(image);

            // Próximo paso:
            // - Align
            // - Encode

            throw new NotImplementedException();
        }
    }
}
