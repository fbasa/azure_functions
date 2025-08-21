namespace ImageFlow.Functions.Abstractions;

using ImageFlow.Functions.Models;

public interface IResultWriter
{
    Task WriteJsonAsync(string name, ImageAnalysisDto dto, CancellationToken ct);
    Task WriteThumbnailAsync(string name, Stream thumbnail, CancellationToken ct);
}

