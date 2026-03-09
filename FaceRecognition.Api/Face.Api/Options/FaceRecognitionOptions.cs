using System.ComponentModel.DataAnnotations;

namespace Face.Api.Options;

public class FaceRecognitionOptions
{
    [Required]
    public string CollectionName { get; set; } = "faces";

    [Range(0, 1)]
    public float DetectionScoreMin { get; set; } = 0.5f;

    [Range(0, 1)]
    public float MatchScoreMin { get; set; } = 0.8f;

    [Range(1, 1000)]
    public int SearchTopK { get; set; } = 20;

    [Range(0, 1)]
    public float AutoUpdateThreshold { get; set; } = 0.8f;

    [Range(0.01, 0.99)]
    public float AutoUpdateLiveWeight { get; set; } = 0.1f;

    [Range(0, 10)]
    public int TemplatesPerModality { get; set; } = 1;
}
