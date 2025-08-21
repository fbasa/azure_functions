using ImageFlow.Functions.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace ImageFlow.Functions.Services;

public sealed class ImageSharpThumbnailGenerator : IThumbnailGenerator
{
    public async Task<Stream> GenerateAsync(Stream image, int maxWidth, int maxHeight, CancellationToken ct)
    {
        image.Position = 0;
        using var img = await Image.LoadAsync(image, ct);
        img.Mutate(x => x.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(maxWidth, maxHeight)
        }));

        var ms = new MemoryStream();
        await img.SaveAsJpegAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }
}
