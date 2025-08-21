using Azure.Storage.Blobs;
using ImageFlow.Functions.Abstractions;
using ImageFlow.Functions.Options;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Security.Cryptography;

namespace ImageFlow.Functions;

public sealed class ProcessImageFunction(
        ILogger<ProcessImageFunction> log,
        BlobServiceClient svc,
        StorageOptions storageOptions,
        IBlobLock blobLock,
        IIdempotencyStore idemp,
        IImageAnalyzer analyzer,
        IThumbnailGenerator thumbnails,
        IResultWriter writer)
{
    [Function("process-image")]
    public async Task RunAsync(
        [BlobTrigger("%INPUT_CONTAINER%/{name}", Connection = "AzureWebJobsStorage")]
        Stream blobStream,
        string name,
        FunctionContext ctx,
        CancellationToken ct)
    {
        // Quickly bail out if not an image (content-type/extension)
        if (!name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) &&
            !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
            !name.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) &&
            !name.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) &&
            !name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
        {
            log.LogInformation("Skipping non-image blob {Name}", name);
            return;
        }

        // Compute SHA256 without loading all to memory
        var sha256 = await ComputeSha256Async(blobStream, ct);
        log.LogInformation("Blob {Name} hash {Hash}", name, sha256);

        // Idempotency check (processed + matching hash)
        var (already, existingHash) = await idemp.CheckAsync(storageOptions.InputContainer, name, ct);
        if (already && string.Equals(existingHash, sha256, StringComparison.Ordinal))
        {
            log.LogInformation("Skip {Name} - already processed with same hash.", name);
            return;
        }

        // Acquire lease lock (avoid concurrent processing)
        await using var lease = await blobLock.TryAcquireAsync(storageOptions.InputContainer, name, ct);
        if (lease is null)
        {
            log.LogWarning("Could not acquire lease for {Name}. Another worker is processing.", name);
            return;
        }

        // Reopen blob stream fresh for downstream consumers
        var container = svc.GetBlobContainerClient(storageOptions.InputContainer);
        var blob = container.GetBlobClient(name);

        var resp = await blob.DownloadStreamingAsync(cancellationToken: ct);
        await using var imageStream = resp.Value.Content;  // dispose this, not resp

        // (optional) also dispose the raw response explicitly
        using var _ = resp.GetRawResponse();

        // Analyze (Computer Vision)
        var dto = await analyzer.AnalyzeAsync(imageStream, ct);

        // Generate thumbnail
        imageStream.Position = 0;
        using var thumb = await thumbnails.GenerateAsync(imageStream, maxWidth: 512, maxHeight: 512, ct);

        // Persist results
        await writer.WriteJsonAsync(name, dto, ct);
        await writer.WriteThumbnailAsync(name, thumb, ct);

        // Mark processed (idempotency)
        await idemp.MarkProcessedAsync(storageOptions.InputContainer, name, sha256, ct);

        log.LogInformation("Processed image {Name} successfully.", name);
    }

    private static async Task<string> ComputeSha256Async(Stream s, CancellationToken ct)
    {
        s.Position = 0;
        using var sha = SHA256.Create();
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            int read;
            while ((read = await s.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                sha.TransformBlock(buffer, 0, read, null, 0);

            sha.TransformFinalBlock([], 0, 0);
            return Convert.ToHexString(sha.Hash!);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            s.Position = 0;
        }
    }
}
