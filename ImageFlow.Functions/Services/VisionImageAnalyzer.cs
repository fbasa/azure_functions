using Azure;
using Azure.AI.Vision.ImageAnalysis;
using Azure.Security.KeyVault.Secrets;
using ImageFlow.Functions.Abstractions;
using ImageFlow.Functions.Models;
using ImageFlow.Functions.Options;
using Polly;
using Polly.Retry;

namespace ImageFlow.Functions.Services;

public sealed class VisionImageAnalyzer : IImageAnalyzer
{
    private readonly SecretClient _kv;
    private readonly VisionOptions _v;
    private readonly AsyncRetryPolicy _retry;

    public VisionImageAnalyzer(SecretClient kv, VisionOptions v)
    {
        _kv = kv; _v = v;

        // Retry the outbound call with jitter to handle throttling/transients
        _retry = Policy
            .Handle<RequestFailedException>()
            .Or<OperationCanceledException>()
            .WaitAndRetryAsync(3, i => TimeSpan.FromSeconds(Math.Pow(2, i)) + TimeSpan.FromMilliseconds(Random.Shared.Next(100, 500)));
    }

    public async Task<ImageAnalysisDto> AnalyzeAsync(Stream image, CancellationToken ct)
    {
        var endpoint = (await _kv.GetSecretAsync(_v.EndpointSecretName, cancellationToken: ct)).Value.Value;
        var key = (await _kv.GetSecretAsync(_v.KeySecretName, cancellationToken: ct)).Value.Value;

        var client = new ImageAnalysisClient(new Uri(endpoint), new AzureKeyCredential(key));

        var result = await _retry.ExecuteAsync(
            token => client.AnalyzeAsync(
                BinaryData.FromStream(image),
                VisualFeatures.Caption | VisualFeatures.Tags | VisualFeatures.Objects | VisualFeatures.People,
                new ImageAnalysisOptions { GenderNeutralCaption = true },
                token),
            ct);

        var v = result.Value;
        var objs = new List<string>();
        foreach (var o in v.Objects.Values)
        {
            if(o.Tags.Count > 0)
            {
                objs.AddRange(o.Tags.Select(t => t.Name).ToList());
            }
        }
        return new ImageAnalysisDto
        {
            BlobName = "<unknown>",
            Caption = v.Caption?.Text ??
                      v.DenseCaptions?.Values?.FirstOrDefault()?.Text ?? "",
            CaptionConfidence = v.Caption?.Confidence ??
                                v.DenseCaptions?.Values?.FirstOrDefault()?.Confidence ?? 0,
            Tags = v.Tags?.Values?.Select(t => t.Name).ToList() ?? new(),
            Objects = v.Objects?.Values?
                         .Where(o => o.Tags is { Count: > 0 })
                         .SelectMany(o => o.Tags!)
                         .Select(t => t.Name)
                         .Where(n => !string.IsNullOrWhiteSpace(n))
                         .Distinct(StringComparer.OrdinalIgnoreCase)   // remove dup names (optional)
                         .ToList()
                      ?? new List<string>(),
            Width = v.Metadata?.Width,
            Height = v.Metadata?.Height
        };
    }
}

