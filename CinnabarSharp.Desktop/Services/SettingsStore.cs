using System;
using System.IO;
using System.Text.Json;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;

namespace CinnabarSharp.Desktop.Services;

/// <summary>Everything remembered between sessions (besides recent files).</summary>
public record AppSettings
{
    public double? WindowWidth { get; init; }
    public double? WindowHeight { get; init; }
    public int? WindowX { get; init; }
    public int? WindowY { get; init; }
    public bool WindowMaximized { get; init; }

    public string? SelectedTool { get; init; }
    public uint PrimaryColor { get; init; } = ColorBgra.Black.Bgra;
    public uint SecondaryColor { get; init; } = ColorBgra.White.Bgra;
    public int BrushWidth { get; init; } = 2;
    public bool Antialiasing { get; init; } = true;
    public int Tolerance { get; init; } = 50;
    public bool GlobalFill { get; init; }
    public bool SampleImage { get; init; }
    public SelectionMode SelectionMode { get; init; }
    public ShapeKind ShapeKind { get; init; }
    public ShapeStyle ShapeStyle { get; init; }
    public GradientKind GradientKind { get; init; }
}

/// <summary>Reads and writes <see cref="AppSettings"/> as JSON in the user's app-data folder; never throws.</summary>
public class SettingsStore(string path)
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CinnabarSharp", "settings.json");

    public AppSettings Load()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
