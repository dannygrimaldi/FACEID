using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Face.Api.Core.FacePipeline.Recognition
{
    public sealed class ArcFaceRecognizer : IArcFaceRecognizer, IDisposable
    {
        private readonly InferenceSession _session;
        private readonly string _inputName;
        private readonly string _outputName;

        public ArcFaceRecognizer(IWebHostEnvironment env)
        {
            var modelPath = Path.Combine(
                env.ContentRootPath,
                "Models",
                "w600k_r50.onnx"
            );

            if (!File.Exists(modelPath))
                throw new FileNotFoundException("ArcFace model not found", modelPath);

            var options = new Microsoft.ML.OnnxRuntime.SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            _session = new InferenceSession(modelPath, options);

            _inputName = _session.InputMetadata.Keys.First();
            _outputName = _session.OutputMetadata.Keys.First();
        }

        public float[] ExtractEmbedding(DenseTensor<float> input)
        {
            var inputs = new[]
            {
                NamedOnnxValue.CreateFromTensor(_inputName, input)
            };

            using var results = _session.Run(inputs);

            return results
                .First()
                .AsEnumerable<float>()
                .ToArray();
        }

        public void Dispose()
        {
            _session.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
