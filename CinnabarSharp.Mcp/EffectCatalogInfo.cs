using CinnabarSharp.Core.Adjustments;
using CinnabarSharp.Core.Effects;

namespace CinnabarSharp.Mcp;

public sealed record EffectParameterInfo(string Name, double Minimum, double Maximum, double Default, double Step,
    IReadOnlyList<string>? Choices);

public sealed record EffectInfo(string Name, string Menu, IReadOnlyList<EffectParameterInfo> Parameters,
    bool CanSuggestValues, string? ValuesEncoding);

/// <summary>The adjustments and effects an agent can run, described from <see cref="Effect.Parameters"/>.</summary>
public static class EffectCatalogInfo
{
    public static IReadOnlyList<Effect> Everything { get; } =
        [.. EffectCatalog.Adjustments, .. EffectCatalog.Effects, .. EffectCatalog.PhotoTools];

    public static IReadOnlyList<EffectInfo> All { get; } = Everything.Select(Describe).ToList();

    private static EffectInfo Describe(Effect effect) => new(
        effect.Name,
        EffectCatalog.Adjustments.Contains(effect) ? "Adjustments"
        : EffectCatalog.PhotoTools.Contains(effect) ? "Photo"
        : $"Effects › {effect.Category}",
        effect.Defaults.Count == effect.Parameters.Count
            ? effect.Parameters.Select(p => new EffectParameterInfo(p.Name, p.Minimum, p.Maximum, p.Default, p.Step, p.Choices)).ToList()
            : [],
        effect.GetType().GetMethod(nameof(Effect.SuggestValues))!.DeclaringType != typeof(Effect),
        effect switch
        {
            Curves => "Pass 'values': mode (0 = luminosity, 1 = RGB), then for each curve (luminosity, red, green, blue) " +
                      "its point count followed by x, y pairs in 0-255. Identity: [0, 2,0,0,255,255, 2,0,0,255,255, 2,0,0,255,255, 2,0,0,255,255].",
            Levels => "Named parameters apply the same levels to every channel; or pass 15 'values': " +
                      "input black, input white, gamma, output black, output white for red, then green, then blue.",
            _ => null,
        });
}
