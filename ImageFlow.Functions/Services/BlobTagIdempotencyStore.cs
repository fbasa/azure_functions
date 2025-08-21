using Azure;
using Azure.Storage.Blobs;
using ImageFlow.Functions.Abstractions;

namespace ImageFlow.Functions.Services;

public sealed class BlobTagIdempotencyStore : IIdempotencyStore
{
    private readonly BlobServiceClient _svc;
    private readonly string _container;
    public BlobTagIdempotencyStore(BlobServiceClient svc, string inputContainer)
    {
        _svc = svc;
        _container = inputContainer;
    }

    public async Task<(bool AlreadyProcessed, string? ExistingHash)>
        CheckAsync(string container, string blobName, CancellationToken ct)
    {
        var client = _svc.GetBlobContainerClient(container).GetBlobClient(blobName);
        try
        {
            var tags = await client.GetTagsAsync(cancellationToken: ct);
            var dict = tags.Value.Tags;
            var processed = dict.TryGetValue("processed", out var p) && p == "true";
            dict.TryGetValue("sha256", out var hash);
            return (processed, hash);
        }
        catch (RequestFailedException)
        {
            return (false, null);
        }
    }

    public async Task MarkProcessedAsync(string container, string blobName, string sha256, CancellationToken ct)
    {
        var client = _svc.GetBlobContainerClient(container).GetBlobClient(blobName);
        var tags = new Dictionary<string, string>
        {
            ["processed"] = "true",
            ["sha256"] = sha256
        };
        await client.SetTagsAsync(tags, cancellationToken: ct);
    }
}

