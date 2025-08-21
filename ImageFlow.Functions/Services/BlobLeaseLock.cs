using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using ImageFlow.Functions.Abstractions;

namespace ImageFlow.Functions.Services;

public sealed class BlobLeaseLock : IBlobLock
{
    private readonly BlobServiceClient _svc;
    public BlobLeaseLock(BlobServiceClient svc) => _svc = svc;

    public async Task<IAsyncDisposable?> TryAcquireAsync(string container, string blobName, CancellationToken ct)
    {
        var client = _svc.GetBlobContainerClient(container).GetBlobClient(blobName);
        var leaseClient = client.GetBlobLeaseClient();

        try
        {
            var lease = await leaseClient.AcquireAsync(TimeSpan.FromSeconds(60), cancellationToken: ct);
            return new Releaser(leaseClient);
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == "LeaseAlreadyPresent")
        {
            return null; // someone else is processing
        }
    }

    private sealed class Releaser : IAsyncDisposable
    {
        private readonly BlobLeaseClient _leaseClient;
        public Releaser(BlobLeaseClient leaseClient) => _leaseClient = leaseClient;
        public async ValueTask DisposeAsync() => await _leaseClient.ReleaseAsync();
    }
}
