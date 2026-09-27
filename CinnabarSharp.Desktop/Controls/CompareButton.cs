using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace CinnabarSharp.Desktop.Controls;

/// <summary>Shows the image before the effect while the button is held down (before/after comparison).</summary>
public class CompareButton : Button
{
    public static readonly StyledProperty<bool> ShowOriginalProperty =
        AvaloniaProperty.Register<CompareButton, bool>(nameof(ShowOriginal), defaultBindingMode: BindingMode.TwoWay);

    protected override Type StyleKeyOverride => typeof(Button);

    public CompareButton()
    {
        Content = "Hold to compare";
        ToolTip.SetTip(this, "Shows the original image while pressed");
    }

    public bool ShowOriginal
    {
        get => GetValue(ShowOriginalProperty);
        set => SetValue(ShowOriginalProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPressedProperty)
            ShowOriginal = IsPressed;
    }
}
