using System.Text.RegularExpressions;

namespace MinecraftServerManager.Services;

/// <summary>Naming and layout rules for world folders inside the server directory.</summary>
public static partial class WorldFolders
{
    /// <summary>Bukkit/Paper store the Nether and End next to the main world as "{world}_nether" and "{world}_the_end".</summary>
    private static readonly string[] DimensionSuffixes = ["_nether", "_the_end"];

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static readonly HashSet<string> ServerFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "backups", "logs", "libraries", "versions", "crash-reports", "config", "mods", "plugins", "debug", ".manager",
    };

    public static bool IsWorldDirectory(string path) => File.Exists(Path.Combine(path, "level.dat"));

    /// <summary>The main world folder plus any split dimension folders that exist.</summary>
    public static List<string> GetExistingFolders(string serverDirectory, string world)
    {
        var folders = new List<string>();
        foreach (var name in DimensionSuffixes.Select(s => world + s).Prepend(world))
        {
            var path = Path.Combine(serverDirectory, name);
            if (Directory.Exists(path)) folders.Add(path);
        }
        return folders;
    }

    /// <summary>True if <paramref name="folderName"/> is a split dimension folder of another world in <paramref name="worldNames"/>.</summary>
    public static bool IsDimensionFolder(string folderName, ISet<string> worldNames) =>
        DimensionSuffixes.Any(suffix =>
            folderName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            && worldNames.Contains(folderName[..^suffix.Length]));

    /// <summary>True if <paramref name="folderName"/> is <paramref name="world"/> or one of its dimension folders.</summary>
    public static bool BelongsTo(string folderName, string world) =>
        string.Equals(folderName, world, StringComparison.OrdinalIgnoreCase)
        || DimensionSuffixes.Any(s => string.Equals(folderName, world + s, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns a user-facing error message, or null if <paramref name="name"/> is a valid world folder name.</summary>
    public static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Enter a world name.";
        if (name.Length > 64) return "World names can be at most 64 characters.";
        if (!NameRegex().IsMatch(name)) return "Use only letters, numbers, spaces, dashes, underscores and dots.";
        if (name != name.Trim() || name.StartsWith('.') || name.EndsWith('.')) return "World names can't start or end with a space or dot.";
        if (WindowsReservedNames.Contains(name.Split('.')[0])) return $"'{name}' is a reserved name on Windows.";
        if (ServerFolderNames.Contains(name)) return $"'{name}' is used by the server for other files.";
        return null;
    }

    /// <summary>Turns an arbitrary string (such as an uploaded file name) into a valid world name.</summary>
    public static string Sanitize(string raw)
    {
        var cleaned = InvalidCharsRegex().Replace(raw, "_").Trim(' ', '.');
        if (cleaned.Length > 64) cleaned = cleaned[..64].Trim(' ', '.');
        return cleaned.Length == 0 ? "imported-world" : cleaned;
    }

    [GeneratedRegex("^[A-Za-z0-9 _.-]+$")]
    private static partial Regex NameRegex();

    [GeneratedRegex("[^A-Za-z0-9 _.-]")]
    private static partial Regex InvalidCharsRegex();
}
