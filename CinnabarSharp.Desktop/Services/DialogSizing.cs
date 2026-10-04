using System;
using Avalonia.Controls;

namespace CinnabarSharp.Desktop.Services;

/// <summary>
/// Keeps a dialog inside the screen: on a 200 % display of 1920 × 1080 only 540 logical pixels are left, less than
/// Adjust Photo needs. The dialog's content scrolls (a ScrollViewer around it) once the window reaches this limit.
/// </summary>
public static class DialogSizing
{
    /// <summary>Room taken by the title bar and a margin the window must leave on screen.</summary>
    private const double FrameAllowance = 72;

    /// <summary>Even on a big screen a dialog this tall is a wall of sliders: it scrolls instead.</summary>
    private const double MaxDialogHeight = 760;

    public static void FitToScreen(Window window) => window.Opened += (_, _) => Apply(window);

    public static void Apply(Window window)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen is null)
            return;
        Apply(window, screen.WorkingArea.Height / screen.Scaling);
    }

    /// <summary>Limits the window to a screen <paramref name="logicalScreenHeight"/> pixels high.</summary>
    public static void Apply(Window window, double logicalScreenHeight) =>
        window.MaxHeight = Math.Clamp(logicalScreenHeight - FrameAllowance, 240, MaxDialogHeight);
}
