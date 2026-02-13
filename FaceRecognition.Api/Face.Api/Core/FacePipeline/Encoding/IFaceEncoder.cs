namespace Face.Api.Core.FacePipeline.Encoding
{
    public interface IFaceEncoder
    {
        float[] Encode(byte[] alignedFaceImage);
    }
}
