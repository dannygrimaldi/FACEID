using Microsoft.ML.OnnxRuntime.Tensors;

public sealed class ScrfdImageInfo
{
    public DenseTensor<float> Tensor { get; init; } = default!;
    public float Scale { get; init; }
    public int PadX { get; init; }
    public int PadY { get; init; }
}
