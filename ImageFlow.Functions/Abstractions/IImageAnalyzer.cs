namespace ImageFlow.Functions.Abstractions;

using ImageFlow.Functions.Models;

public interface IImageAnalyzer
{
    Task<ImageAnalysisDto> AnalyzeAsync(Stream image, CancellationToken ct);
}

