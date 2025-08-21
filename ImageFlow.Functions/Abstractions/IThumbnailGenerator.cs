namespace ImageFlow.Functions.Abstractions;

public interface IThumbnailGenerator
{
    Task<Stream> GenerateAsync(Stream image, int maxWidth, int maxHeight, CancellationToken ct);
}

