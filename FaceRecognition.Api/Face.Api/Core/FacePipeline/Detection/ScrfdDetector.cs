using Face.Api.Core.FacePipeline.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Linq;

namespace Face.Api.Core.FacePipeline.Detection
{
    public class ScrfdDetector : IFaceDetector, IDisposable
    {
        private readonly ILogger<ScrfdDetector> _logger;
        private readonly InferenceSession _session;

        public ScrfdDetector(
            string modelPath,
            ILogger<ScrfdDetector> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (!File.Exists(modelPath))
                throw new FileNotFoundException($"SCRFD model not found: {modelPath}");

            var options = new Microsoft.ML.OnnxRuntime.SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            _session = new InferenceSession(modelPath, options);
        }

        public FaceDetection Detect(Image<Rgb24> image)
        {
            ArgumentNullException.ThrowIfNull(image);

            // 1️⃣ Preprocesar imagen (letterbox)
            var prep = ScrfdPreprocessor.ToTensor(image);

            // 2️⃣ Preparar input ONNX
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input.1", prep.Tensor)
            };

            // 3️⃣ Ejecutar inferencia
            using var outputs = _session.Run(inputs);

            // Debug opcional (esto SÍ es enumerable)
            foreach (var o in outputs)
            {
                var tensor = o.AsTensor<float>();
                _logger.LogInformation(
                    "SCRFD OUTPUT -> {Name} : [{Dims}]",
                    o.Name,
                    string.Join(",", tensor.Dimensions.ToArray())
                );
            }

            // 4️⃣ Post-procesar (undo letterbox)
            return ScrfdPostProcessor.Parse(
                outputs,
                prep,
                image.Width,
                image.Height
            );
        }

        public void Dispose()
        {
            _session.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
