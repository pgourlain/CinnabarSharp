using Microsoft.Extensions.DependencyInjection;
using CinnabarSharp.Core.Effects;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Core.Tests;

/// <summary>
/// EffectSession.Compute runs the region as parallel horizontal strips (performance-tasks.md P3). This pins that
/// every effect/adjustment/photo-tool gives bit-identical results whether it ran as one block or as several
/// strips on separate threads. The image is tall enough (with the small MinStripRows below) to guarantee more
/// than one strip actually runs, for every effect in the catalog.
/// </summary>
public sealed class EffectParallelismTests : BaseTests
{
    private const int Width = 40, Height = 200;

    private readonly IServiceProvider _sp;
    private readonly IWorkspaceService _workspace;

    public EffectParallelismTests()
    {
        _sp = CinnabarSharpService();
        _workspace = _sp.GetRequiredService<IWorkspaceService>();
    }

    public static TheoryData<string> EffectNames => new(
        EffectCatalog.All.Concat(EffectCatalog.Adjustments).Select(e => e.Name));

    private static Effect Find(string name) =>
        EffectCatalog.All.Concat(EffectCatalog.Adjustments).Single(e => e.Name == name);

    private static byte[] Pattern()
    {
        var px = new byte[Width * Height * 4];
        for (var i = 0; i < Width * Height; i++)
            (px[i * 4], px[i * 4 + 1], px[i * 4 + 2], px[i * 4 + 3]) =
                ((byte)(i * 13), (byte)(i * 7), (byte)(i * 3), (byte)(255 - i % 50));
        return px;
    }

    [Theory]
    [MemberData(nameof(EffectNames))]
    public void Parallel_strips_match_a_single_block(string name)
    {
        var effect = Find(name);
        var doc = _workspace.NewDocument(new ImageSize(Width, Height), ColorBgra.White);
        var old = doc.Layers.CurrentUserLayer.Surface;
        doc.Layers.CurrentUserLayer.Surface = Utility.FromBgra(Pattern(), Width, Height);
        old.Dispose();

        var session = new EffectSession(doc, effect);
        var strips = session.Compute(effect.Defaults);

        var singleBlock = new byte[Width * Height * 4];
        effect.Render(session.Context, new RectangleI(0, 0, Width, Height), singleBlock, effect.Defaults, CancellationToken.None);

        Assert.Equal(singleBlock, strips);
    }
}
