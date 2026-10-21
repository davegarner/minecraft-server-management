using System.IO.Compression;

namespace MinecraftServerManager.Util;

/// <summary>Stops archives from filling the disk ("zip bombs").</summary>
public static class SafeZip
{
    /// <summary>
    /// Throws if <paramref name="entries"/> declare more data than there is free space at <paramref name="destination"/>.
    /// .NET never decompresses an entry past its declared size, so the declared total is a real upper bound.
    /// </summary>
    public static void EnsureSpaceFor(IEnumerable<ZipArchiveEntry> entries, string destination)
    {
        var needed = entries.Sum(e => e.Length);
        if (FileUtil.GetFreeSpace(destination) is { } free && needed > free)
            throw new IOException($"Not enough free disk space to extract the archive ({Fmt.Bytes(needed)} needed, {Fmt.Bytes(free)} available).");
    }
}
