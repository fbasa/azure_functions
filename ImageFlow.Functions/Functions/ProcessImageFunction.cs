using Azure.Storage.Blobs;
using ImageFlow.Functions.Abstractions;
using ImageFlow.Functions.Options;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Security.Cryptography;

namespace ImageFlow.Functions.Functions;

public sealed class ProcessImageFunction
{
    private readonly ILogger<ProcessImageFunction> _log;
    private readonly BlobServiceClient _svc;
    private readonly StorageOptions _s;
    private readonly IBlobLock _lock;
    private readonly IIdempotencyStore _idemp;
    private readonly IImageAnalyzer _analyzer;
    private readonly IThumbnailGenerator _thumbnails;
    private readonly IResultWriter _writer;

    public ProcessImageFunction(
        ILogger<ProcessImageFunction> log,
        BlobServiceClient svc,
        StorageOptions s,
        IBlobLock blobLock,
        IIdempotencyStore idempotency,
        IImageAnalyzer analyzer,
        IThumbnailGenerator thumbnails,
        IResultWriter writer)
    {
        _log = log; _svc = svc; _s = s;
        _lock = blobLock; _idemp = idempotency;
        _analyzer = analyzer; _thumbnails = thumbnails; _writer = writer;
    }

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
            _log.LogInformation("Skipping non-image blob {Name}", name);
            return;
        }

        // Compute SHA256 without loading all to memory
        var sha256 = await ComputeSha256Async(blobStream, ct);
        _log.LogInformation("Blob {Name} hash {Hash}", name, sha256);

        // Idempotency check (processed + matching hash)
        var (already, existingHash) = await _idemp.CheckAsync(_s.InputContainer, name, ct);
        if (already && string.Equals(existingHash, sha256, StringComparison.Ordinal))
        {
            _log.LogInformation("Skip {Name} - already processed with same hash.", name);
            return;
        }

        // Acquire lease lock (avoid concurrent processing)
        await using var lease = await _lock.TryAcquireAsync(_s.InputContainer, name, ct);
        if (lease is null)
        {
            _log.LogWarning("Could not acquire lease for {Name}. Another worker is processing.", name);
            return;
        }

        // Reopen blob stream fresh for downstream consumers
        var container = _svc.GetBlobContainerClient(_s.InputContainer);
        var blob = container.GetBlobClient(name);

        var resp = await blob.DownloadStreamingAsync(cancellationToken: ct);
        await using var imageStream = resp.Value.Content;  // dispose this, not resp

        // (optional) also dispose the raw response explicitly
        using var _ = resp.GetRawResponse();

        // Analyze (Computer Vision)
        var dto = await _analyzer.AnalyzeAsync(imageStream, ct);

        // Generate thumbnail
        imageStream.Position = 0;
        using var thumb = await _thumbnails.GenerateAsync(imageStream, maxWidth: 512, maxHeight: 512, ct);

        // Persist results
        await _writer.WriteJsonAsync(name, dto, ct);
        await _writer.WriteThumbnailAsync(name, thumb, ct);

        // Mark processed (idempotency)
        await _idemp.MarkProcessedAsync(_s.InputContainer, name, sha256, ct);

        _log.LogInformation("Processed image {Name} successfully.", name);
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
