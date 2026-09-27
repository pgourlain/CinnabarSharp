using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CinnabarSharp.Desktop.Services;

/// <summary>Most-recently-used image paths, persisted as JSON in the user's app-data folder.</summary>
public class RecentFilesStore
{
    public const int MaxCount = 10;

    private readonly string _path;
    private List<string> _files;

    public RecentFilesStore(string path)
    {
        _path = path;
        _files = Load(path);
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CinnabarSharp", "recent-files.json");

    public IReadOnlyList<string> Files => _files;

    public event Action? Changed;

    public void Add(string path)
    {
        var full = Path.GetFullPath(path);
        _files = [full, .. _files.Where(f => f != full).Take(MaxCount - 1)];
        SaveAndNotify();
    }

    public void Remove(string path)
    {
        if (_files.Remove(Path.GetFullPath(path)))
            SaveAndNotify();
    }

    public void Clear()
    {
        _files = [];
        SaveAndNotify();
    }

    private void SaveAndNotify()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_files));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        Changed?.Invoke();
    }

    private static List<string> Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? []
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
}
