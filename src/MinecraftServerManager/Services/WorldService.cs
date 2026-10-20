using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using MinecraftServerManager.Util;

namespace MinecraftServerManager.Services;

public sealed record WorldInfo(string Name, string Path, long SizeBytes, DateTime LastModified, bool IsActive, LevelInfo? Level);

public sealed class NewWorldOptions
{
    public string Name { get; set; } = "";
    public string Seed { get; set; } = "";
    public string LevelType { get; set; } = "minecraft:normal";
    public string GeneratorSettings { get; set; } = "";
    public bool GenerateStructures { get; set; } = true;
    public bool Hardcore { get; set; }
    public string Difficulty { get; set; } = "easy";
    public string GameMode { get; set; } = "survival";
    public bool StartServer { get; set; } = true;
}

/// <summary>
/// Lists, creates, switches, imports and deletes worlds. Vanilla Minecraft generates a world the first time
/// the server starts with a new level-name, so creating a world means configuring server.properties and (re)starting.
/// </summary>
public sealed class WorldService(
    SettingsService settings,
    ServerPropertiesService properties,
    ServerProcessService server,
    BackupService backups,
    ILogger<WorldService> logger)
{
    private static readonly TimeSpan SizeCacheDuration = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, long Size)> _sizeCache = new(StringComparer.OrdinalIgnoreCase);

    public string ActiveWorld => properties.LevelName;

    public IReadOnlyList<WorldInfo> ListWorlds()
    {
        var serverDirectory = settings.Current.ServerDirectory;
        if (!Directory.Exists(serverDirectory)) return [];

        var active = properties.LevelName;
        var names = Directory.EnumerateDirectories(serverDirectory)
            .Where(WorldFolders.IsWorldDirectory)
            .Select(d => Path.GetFileName(d))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var worlds = new List<WorldInfo>();
        foreach (var name in names)
        {
            if (WorldFolders.IsDimensionFolder(name, names)) continue;
            var path = Path.Combine(serverDirectory, name);
            var levelDat = Path.Combine(path, "level.dat");
            worlds.Add(new WorldInfo(
                name,
                path,
                WorldFolders.GetExistingFolders(serverDirectory, name).Sum(GetCachedSize),
                File.GetLastWriteTime(levelDat),
                string.Equals(name, active, StringComparison.OrdinalIgnoreCase),
                LevelInfo.TryRead(levelDat)));
        }
        return worlds
            .OrderByDescending(w => w.IsActive)
            .ThenBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool WorldExists(string name) =>
        Directory.Exists(Path.Combine(settings.Current.ServerDirectory, name));

    public async Task CreateWorldAsync(NewWorldOptions options)
    {
        var name = options.Name.Trim();
        if (WorldFolders.ValidateName(name) is { } error) throw new InvalidOperationException(error);
        if (WorldExists(name)) throw new InvalidOperationException($"A folder named '{name}' already exists in the server directory.");

        var generatorSettings = string.IsNullOrWhiteSpace(options.GeneratorSettings) ? "{}" : options.GeneratorSettings.Trim();
        try
        {
            using var _ = JsonDocument.Parse(generatorSettings);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Generator settings must be valid JSON.");
        }

        var values = new Dictionary<string, string>
        {
            ["level-name"] = name,
            ["level-seed"] = options.Seed.Trim(),
            ["level-type"] = options.LevelType,
            ["generator-settings"] = generatorSettings,
            ["generate-structures"] = options.GenerateStructures ? "true" : "false",
            ["hardcore"] = options.Hardcore ? "true" : "false",
            ["difficulty"] = options.Hardcore ? "hard" : options.Difficulty,
            ["gamemode"] = options.GameMode,
        };

        var restart = options.StartServer && server.IsRunning;
        if (restart) await server.StopServerAsync();
        properties.Update(values);
        logger.LogInformation("Configured new world {World} ({Type}, seed '{Seed}')", name, options.LevelType, options.Seed);
        server.AddManagerLine($"New world '{name}' configured. It will be generated when the server starts.");
        if (options.StartServer) await server.StartServerAsync();
    }

    /// <summary>Makes <paramref name="name"/> the world the server loads, optionally restarting a running server.</summary>
    public async Task SetActiveWorldAsync(string name, bool restartIfRunning)
    {
        if (!WorldExists(name)) throw new InvalidOperationException($"World '{name}' was not found.");
        var restart = restartIfRunning && server.IsRunning;
        if (restart) await server.StopServerAsync();
        properties.Update(new Dictionary<string, string> { ["level-name"] = name });
        server.AddManagerLine($"Active world set to '{name}'.");
        if (restart) await server.StartServerAsync();
    }

    public async Task DeleteWorldAsync(string name, bool backupFirst)
    {
        var serverDirectory = settings.Current.ServerDirectory;
        var folders = WorldFolders.GetExistingFolders(serverDirectory, name);
        if (folders.Count == 0) throw new InvalidOperationException($"World '{name}' was not found.");
        if (server.IsRunning && string.Equals(name, properties.LevelName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Stop the server before deleting the world it is currently running.");

        if (backupFirst) await backups.CreateAsync(name, BackupKind.PreDelete);
        foreach (var folder in folders)
        {
            FileUtil.DeleteDirectory(folder);
            _sizeCache.TryRemove(folder, out _);
        }
        logger.LogInformation("Deleted world {World}", name);
        server.AddManagerLine($"World '{name}' deleted.");
    }

    /// <summary>Imports a zipped world (e.g. a singleplayer save). The folder containing level.dat becomes the world root.</summary>
    public async Task<string> ImportWorldAsync(Stream zipStream, string name, bool makeActive)
    {
        name = name.Trim();
        if (WorldFolders.ValidateName(name) is { } error) throw new InvalidOperationException(error);
        var current = settings.Current;
        var target = Path.Combine(current.ServerDirectory, name);
        if (Directory.Exists(target)) throw new InvalidOperationException($"A folder named '{name}' already exists in the server directory.");

        var tempDirectory = Path.Combine(current.ManagerDirectory, "tmp");
        Directory.CreateDirectory(tempDirectory);
        var tempZip = Path.Combine(tempDirectory, $"import-{Guid.NewGuid():N}.zip");
        var staging = Path.Combine(tempDirectory, $"import-{Guid.NewGuid():N}");
        try
        {
            await using (var file = File.Create(tempZip)) await zipStream.CopyToAsync(file);
            await Task.Run(() => ExtractWorld(tempZip, staging));
            Directory.Move(staging, target);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Import failed: {ex.Message}", ex);
        }
        finally
        {
            if (File.Exists(tempZip)) File.Delete(tempZip);
            if (Directory.Exists(staging)) FileUtil.DeleteDirectory(staging);
        }

        logger.LogInformation("Imported world {World}", name);
        server.AddManagerLine($"World '{name}' imported.");
        if (makeActive) await SetActiveWorldAsync(name, restartIfRunning: false);
        return name;
    }

    private static void ExtractWorld(string zipPath, string destination)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var levelDat = archive.Entries
            .Select(e => e.FullName.Replace('\\', '/'))
            .Where(n => n.Equals("level.dat", StringComparison.OrdinalIgnoreCase) || n.EndsWith("/level.dat", StringComparison.OrdinalIgnoreCase))
            .Where(n => !n.StartsWith("__MACOSX/", StringComparison.Ordinal))
            .OrderBy(n => n.Count(c => c == '/'))
            .FirstOrDefault()
            ?? throw new InvalidDataException("The archive doesn't contain a Minecraft world (no level.dat was found).");
        var prefix = levelDat[..^"level.dat".Length];

        Directory.CreateDirectory(destination);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.EndsWith('/')) continue;
            var relative = name[prefix.Length..];
            if (relative.Length == 0 || relative.Equals("session.lock", StringComparison.OrdinalIgnoreCase)) continue;

            var path = Path.GetFullPath(Path.Combine(destination, relative));
            if (!FileUtil.IsInside(path, destination))
                throw new InvalidDataException($"The archive contains an unsafe path: {entry.FullName}");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path, overwrite: true);
        }
    }

    private long GetCachedSize(string folder)
    {
        if (_sizeCache.TryGetValue(folder, out var cached) && DateTimeOffset.Now - cached.At < SizeCacheDuration) return cached.Size;
        var size = FileUtil.GetDirectorySize(folder);
        _sizeCache[folder] = (DateTimeOffset.Now, size);
        return size;
    }
}
