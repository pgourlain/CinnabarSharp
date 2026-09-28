using ModelContextProtocol;

namespace CinnabarSharp.Mcp;

/// <summary>
/// Folders the agent may read and write. Paths are compared after resolving symbolic links, so a link inside an
/// allowed folder can't reach outside it. Relative paths are resolved against the first allowed folder; "~" is the
/// user's home folder.
/// </summary>
public sealed class FileAccessPolicy
{
    public FileAccessPolicy(IEnumerable<string> folders)
    {
        AllowedFolders = folders.Select(f => RealPath(Path.GetFullPath(f))).Distinct(PathComparer).ToList();
        if (AllowedFolders.Count == 0)
            throw new ArgumentException("At least one allowed folder is required.", nameof(folders));
    }

    public IReadOnlyList<string> AllowedFolders { get; }

    // macOS and Windows file systems are case-insensitive by default.
    private static readonly StringComparison Comparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer PathComparer => StringComparer.FromComparison(Comparison);

    /// <summary>The full path of <paramref name="path"/>, or an error for the agent if it is outside the allowed folders.</summary>
    public string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new McpException("A path is required.");
        if (path == "~" || path.StartsWith("~/") || path.StartsWith("~\\"))
            path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path[1..];
        var full = RealPath(Path.GetFullPath(path, AllowedFolders[0]));
        if (!IsAllowed(full))
            throw new McpException($"'{path}' is outside the allowed folders ({string.Join(", ", AllowedFolders)}).");
        return full;
    }

    public bool IsAllowed(string fullPath) => AllowedFolders.Any(folder =>
        string.Equals(fullPath, folder, Comparison) ||
        fullPath.StartsWith(Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar, Comparison));

    /// <summary>Resolves symbolic links in every existing part of the path (e.g. /tmp → /private/tmp on macOS).</summary>
    public static string RealPath(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath) ?? "";
        var current = root;
        var parts = fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var next = Path.Combine(current, parts[i]);
            FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
            if (!info.Exists)
                return Path.Combine([next, .. parts[(i + 1)..]]);
            var target = info.LinkTarget is null ? null : info.ResolveLinkTarget(returnFinalTarget: true);
            current = target is null ? next : RealPath(target.FullName);
        }
        return current;
    }
}
