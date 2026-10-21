using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace MinecraftServerManager.Services;

/// <summary>Summary of a world's level.dat.</summary>
public sealed record LevelInfo(string? LevelName, string? Version, long? Seed, int? GameType, bool Hardcore, DateTimeOffset? LastPlayed)
{
    public string GameModeName => GameType switch
    {
        0 => "Survival",
        1 => "Creative",
        2 => "Adventure",
        3 => "Spectator",
        _ => "Unknown",
    };

    public static LevelInfo? TryRead(string levelDatPath)
    {
        try
        {
            if (!File.Exists(levelDatPath)) return null;
            var root = NbtReader.ReadCompressedFile(levelDatPath);
            if (root?.GetValueOrDefault("Data") is not Dictionary<string, object?> data) return null;

            var version = (data.GetValueOrDefault("Version") as Dictionary<string, object?>)?.GetValueOrDefault("Name") as string;
            // 1.16+ stores the seed in WorldGenSettings; older worlds use RandomSeed.
            var seed = (data.GetValueOrDefault("WorldGenSettings") as Dictionary<string, object?>)?.GetValueOrDefault("seed") as long?
                ?? data.GetValueOrDefault("RandomSeed") as long?;
            var lastPlayed = data.GetValueOrDefault("LastPlayed") is long ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : (DateTimeOffset?)null;

            return new LevelInfo(
                data.GetValueOrDefault("LevelName") as string,
                version,
                seed,
                data.GetValueOrDefault("GameType") as int?,
                data.GetValueOrDefault("hardcore") is sbyte hardcore && hardcore != 0,
                lastPlayed);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>Minimal read-only parser for Minecraft's big-endian NBT format.</summary>
internal static class NbtReader
{
    private const int MaxDepth = 256;
    private const int MaxArrayLength = 16 * 1024 * 1024;

    public static Dictionary<string, object?>? ReadCompressedFile(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var buffered = new BufferedStream(gzip);
        return Read(buffered);
    }

    public static Dictionary<string, object?>? Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadByte() != 10) return null; // root must be a compound
        ReadString(reader);
        return ReadCompound(reader, 0);
    }

    private static Dictionary<string, object?> ReadCompound(BinaryReader reader, int depth)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        while (true)
        {
            var type = reader.ReadByte();
            if (type == 0) return result;
            var name = ReadString(reader);
            result[name] = ReadPayload(reader, type, depth + 1);
        }
    }

    private static object? ReadPayload(BinaryReader reader, byte type, int depth)
    {
        if (depth > MaxDepth) throw new InvalidDataException("NBT data is nested too deeply.");
        switch (type)
        {
            case 1: return reader.ReadSByte();
            case 2: return BinaryPrimitives.ReadInt16BigEndian(ReadExactly(reader, 2));
            case 3: return BinaryPrimitives.ReadInt32BigEndian(ReadExactly(reader, 4));
            case 4: return BinaryPrimitives.ReadInt64BigEndian(ReadExactly(reader, 8));
            case 5: return BinaryPrimitives.ReadSingleBigEndian(ReadExactly(reader, 4));
            case 6: return BinaryPrimitives.ReadDoubleBigEndian(ReadExactly(reader, 8));
            case 7: return ReadExactly(reader, ReadLength(reader));
            case 8: return ReadString(reader);
            case 9:
            {
                var itemType = reader.ReadByte();
                var count = ReadLength(reader);
                var list = new List<object?>(Math.Min(count, 1024));
                for (var i = 0; i < count; i++) list.Add(ReadPayload(reader, itemType, depth + 1));
                return list;
            }
            case 10: return ReadCompound(reader, depth);
            case 11:
            {
                var values = new int[ReadLength(reader)];
                for (var i = 0; i < values.Length; i++) values[i] = BinaryPrimitives.ReadInt32BigEndian(ReadExactly(reader, 4));
                return values;
            }
            case 12:
            {
                var values = new long[ReadLength(reader)];
                for (var i = 0; i < values.Length; i++) values[i] = BinaryPrimitives.ReadInt64BigEndian(ReadExactly(reader, 8));
                return values;
            }
            default: throw new InvalidDataException($"Unknown NBT tag type {type}.");
        }
    }

    private static int ReadLength(BinaryReader reader)
    {
        var length = BinaryPrimitives.ReadInt32BigEndian(ReadExactly(reader, 4));
        if (length < 0 || length > MaxArrayLength) throw new InvalidDataException("Invalid NBT array length.");
        return length;
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = BinaryPrimitives.ReadUInt16BigEndian(ReadExactly(reader, 2));
        return Encoding.UTF8.GetString(ReadExactly(reader, length));
    }

    private static byte[] ReadExactly(BinaryReader reader, int count)
    {
        var bytes = reader.ReadBytes(count);
        if (bytes.Length != count) throw new InvalidDataException("Unexpected end of NBT data.");
        return bytes;
    }
}
