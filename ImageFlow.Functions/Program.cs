

using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage.Blobs;
using ImageFlow.Functions.Abstractions;
using ImageFlow.Functions.Options;
using ImageFlow.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureAppConfiguration((ctx, cfg) =>
    {
        cfg.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
        cfg.AddJsonFile($"appsettings.{ctx.HostingEnvironment.EnvironmentName}.json", true);
        cfg.AddEnvironmentVariables();
    })
    .ConfigureServices((ctx, services) =>
    {
        var cfg = ctx.Configuration;

        var storage = new StorageOptions(
            StorageAccountUrl: cfg["STORAGE_ACCOUNT_URL"]!,
            InputContainer: cfg["INPUT_CONTAINER"]!,
            ThumbnailContainer: cfg["THUMBNAIL_CONTAINER"]!,
            AnalysisContainer: cfg["ANALYSIS_CONTAINER"]!);

        var vision = new VisionOptions(
            EndpointSecretName: cfg["VISION_ENDPOINT_SECRET_NAME"]!,
            KeySecretName: cfg["VISION_KEY_SECRET_NAME"]!);

        var kv = new KeyVaultOptions(cfg["KEY_VAULT_URL"]!);

        services.AddSingleton(storage);
        services.AddSingleton(vision);
        services.AddSingleton(kv);

        TokenCredential cred = new DefaultAzureCredential();

        // Storage
        services.AddSingleton(new BlobServiceClient(new Uri(storage.StorageAccountUrl), cred));

        // Key Vault
        services.AddSingleton(new SecretClient(new Uri(kv.KeyVaultUrl), cred));

        // SRP services
        services.AddSingleton<IBlobLock, BlobLeaseLock>();
        services.AddSingleton<IIdempotencyStore>(sp =>
            new BlobTagIdempotencyStore(sp.GetRequiredService<BlobServiceClient>(), storage.InputContainer));
        services.AddSingleton<IImageAnalyzer, VisionImageAnalyzer>();
        services.AddSingleton<IThumbnailGenerator, ImageSharpThumbnailGenerator>();
        services.AddSingleton<IResultWriter>(sp =>
            new BlobResultWriter(sp.GetRequiredService<BlobServiceClient>(), storage.AnalysisContainer, storage.ThumbnailContainer));
    })
    .Build();

await host.RunAsync();
