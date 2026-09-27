using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Desktop.ViewModels;

public record BlendModeOption(BlendMode Mode, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Edits a layer live (the canvas previews every change); <see cref="Revert"/> restores the original values.
/// </summary>
public partial class LayerPropertiesViewModel : ViewModelBase
{
    private readonly UserLayer _layer;
    private readonly (string Name, bool Hidden, BlendMode Mode, double Opacity) _original;

    public LayerPropertiesViewModel(UserLayer layer)
    {
        _layer = layer;
        _original = (layer.Name, layer.Hidden, layer.BlendMode, layer.Opacity);
        Name = layer.Name;
        IsVisible = !layer.Hidden;
        BlendMode = BlendModes.First(b => b.Mode == layer.BlendMode);
        Opacity = Math.Round(layer.Opacity * 100);
    }

    public static IReadOnlyList<BlendModeOption> BlendModes { get; } =
        Enum.GetValues<BlendMode>().Select(m => new BlendModeOption(m, DisplayName(m))).ToList();

    public static string DisplayName(BlendMode mode) => Regex.Replace(mode.ToString(), "(?<=[a-z])(?=[A-Z])", " ");

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    public partial BlendModeOption BlendMode { get; set; }

    /// <summary>0–100.</summary>
    [ObservableProperty]
    public partial double Opacity { get; set; }

    public bool HasChanges =>
        (_layer.Name, _layer.Hidden, _layer.BlendMode, _layer.Opacity) != _original;

    public void Revert()
    {
        _layer.Name = _original.Name;
        _layer.Hidden = _original.Hidden;
        _layer.BlendMode = _original.Mode;
        _layer.Opacity = _original.Opacity;
    }

    partial void OnNameChanged(string value) => _layer.Name = value;
    partial void OnIsVisibleChanged(bool value) => _layer.Hidden = !value;
    partial void OnBlendModeChanged(BlendModeOption value) => _layer.BlendMode = value.Mode;
    partial void OnOpacityChanged(double value) => _layer.Opacity = Math.Clamp(value, 0, 100) / 100;
}
