using System.ComponentModel.DataAnnotations;

namespace Face.Api.Options;

public class RateLimitOptions
{
    [Range(1, 86400)]
    public int WindowSeconds { get; set; } = 60;

    [Range(1, 100000)]
    public int SearchMaxRequests { get; set; } = 3000;

    [Range(1, 100000)]
    public int RegisterMaxRequests { get; set; } = 3000;

    [Range(1, 100000)]
    public int TestDetectMaxRequests { get; set; } = 3000;
}
