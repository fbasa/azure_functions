using Azure.Storage.Blobs;
using ImageFlow.Functions.Abstractions;
using ImageFlow.Functions.Models;
using System.Text.Json;

namespace ImageFlow.Functions.Services;

public sealed class BlobResultWriter(BlobServiceClient svc, string analysisContainer, string thumbContainer) : IResultWriter
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public async Task WriteJsonAsync(string name, ImageAnalysisDto dto, CancellationToken ct)
    {
        dto.BlobName = name;
        var client = svc.GetBlobContainerClient(analysisContainer).GetBlobClient($"{name}.json");
        using var ms = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(dto, JsonOpts));
        await client.UploadAsync(ms, overwrite: true, cancellationToken: ct);
    }

    public async Task WriteThumbnailAsync(string name, Stream thumbnail, CancellationToken ct)
    {
        var client = svc.GetBlobContainerClient(thumbContainer).GetBlobClient(name);
        await client.UploadAsync(thumbnail, overwrite: true, cancellationToken: ct);
    }
}
