using Microsoft.ML.OnnxRuntime.Tensors;

namespace Face.Api.Core.FacePipeline.Recognition
{
    public interface IArcFaceRecognizer
    {
        /// <summary>
        /// Ejecuta inferencia ArcFace y devuelve el embedding crudo (NO normalizado)
        /// </summary>
        float[] ExtractEmbedding(DenseTensor<float> inputTensor);
    }
}
