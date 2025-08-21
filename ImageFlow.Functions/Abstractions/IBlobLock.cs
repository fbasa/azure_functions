namespace ImageFlow.Functions.Abstractions;

public interface IBlobLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(string container, string blobName, CancellationToken ct);
}

