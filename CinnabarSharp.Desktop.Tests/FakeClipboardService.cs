using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Desktop.Tests;

public class FakeClipboardService : IClipboardService
{
    public ClipboardImage? Image { get; set; }

    public Task SetImageAsync(ClipboardImage image)
    {
        Image = image;
        return Task.CompletedTask;
    }

    public Task<ClipboardImage?> GetImageAsync() => Task.FromResult(Image);

    public string? Text { get; set; }

    public Task SetTextAsync(string text)
    {
        Text = text;
        return Task.CompletedTask;
    }

    public Task<string?> GetTextAsync() => Task.FromResult(Text);
}
