using System.Text;

namespace MinecraftServerManager.Util;

public static class FileUtil
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>Writes a file via a temp file + rename so readers never observe a half-written file.</summary>
    public static void WriteAllTextAtomic(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Deletes a directory tree, clearing read-only attributes that would otherwise block deletion on Windows.</summary>
    public static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        // Attributes come from the directory listing, avoiding a file-system call per file for large worlds.
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 };
        foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", options))
        {
            if (file.Attributes.HasFlag(FileAttributes.ReadOnly))
                file.Attributes &= ~FileAttributes.ReadOnly;
        }
        Directory.Delete(path, recursive: true);
    }

    public static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
        return new DirectoryInfo(path).EnumerateFiles("*", options).Sum(f => f.Length);
    }

    /// <summary>True when <paramref name="candidate"/> resolves to a location strictly inside <paramref name="root"/>.</summary>
    public static bool IsInside(string candidate, string root)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(candidate).StartsWith(fullRoot, PathComparison);
    }

    public static long? GetFreeSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return string.IsNullOrEmpty(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
