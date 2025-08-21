namespace ImageFlow.Functions.Abstractions;

public interface IIdempotencyStore
{
    Task<(bool AlreadyProcessed, string? ExistingHash)> CheckAsync(string container, string blobName, CancellationToken ct);
    Task MarkProcessedAsync(string container, string blobName, string sha256, CancellationToken ct);
}

