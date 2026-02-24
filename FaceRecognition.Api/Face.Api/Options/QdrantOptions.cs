using System.ComponentModel.DataAnnotations;

namespace Face.Api.Options;

public class QdrantOptions
{
    [Required]
    [Url]
    public string BaseUrl { get; set; } = default!;

    [Required]
    public string Key { get; set; } = default!;
}