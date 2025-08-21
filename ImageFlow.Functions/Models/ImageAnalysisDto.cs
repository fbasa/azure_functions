
namespace ImageFlow.Functions.Models;

public sealed class ImageAnalysisDto
{
    public required string BlobName { get; set; }
    public required string Caption { get; init; }
    public required float CaptionConfidence { get; init; }
    public List<string> Tags { get; init; } = new();
    public List<string> Objects { get; init; } = new();
    public int? Width { get; init; }
    public int? Height { get; init; }
    public DateTimeOffset ProcessedUtc { get; init; } = DateTimeOffset.UtcNow;
}

