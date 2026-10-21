using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using MinecraftServerManager.Core;
using MinecraftServerManager.Features.Backups;
using MinecraftServerManager.Features.Java;
using MinecraftServerManager.Features.Server;
using MinecraftServerManager.Features.ServerProperties;
using MinecraftServerManager.Features.Settings;
using MinecraftServerManager.Infrastructure.Mojang;
using MinecraftServerManager.Infrastructure;
using MinecraftServerManager.Util;

namespace MinecraftServerManager.Features.Updates;

public sealed record InstalledServer(string? VersionId, string DisplayName, int? JavaMajorVersion, long JarSize, DateTime JarModified);

public sealed record ArchivedJar(string FileName, string Version, DateTime ArchivedAt, long Size);

public sealed record UpdateCheck(InstalledServer? Installed, MinecraftVersion? Latest, bool UpdateAvailable);

public sealed record UpdateProgress(bool IsRunning, string? TargetVersion, string Stage, long BytesDownloaded, long TotalBytes, string? Error, bool Succeeded)
{
    public static readonly UpdateProgress Idle = new(false, null, "", 0, 0, null, false);

    public DateTimeOffset? FinishedAt { get; init; }

    public double Percent => TotalBytes > 0 ? Math.Min(100, 100.0 * BytesDownloaded / TotalBytes) : 0;

    /// <summary>True while installing, and for a while afterwards so the outcome stays visible.</summary>
    public bool IsRecent => IsRunning || (FinishedAt is { } finished && DateTimeOffset.Now - finished < TimeSpan.FromMinutes(10));
}

/// <summary>Installs and updates the vanilla server jar from Mojang's official version manifest.</summary>
public sealed class UpdateService(
    MojangClient mojang,
    SettingsService settings,
    ServerProcessService server,
    ServerPropertiesService properties,
    BackupService backups,
    JavaService java,
    ILogger<UpdateService> logger)
{
    private const int ArchivedJarsToKeep = 5;
    private const string ArchiveTimestampFormat = "yyyyMMdd-HHmmss";
    private static readonly TimeSpan ManifestCacheDuration = TimeSpan.FromMinutes(15);

    private readonly SemaphoreSlim _manifestLock = new(1, 1);
    private readonly SemaphoreSlim _installLock = new(1, 1);
    private readonly Dictionary<string, ServerDownload?> _downloadCache = new(StringComparer.OrdinalIgnoreCase);
    private VersionManifest? _manifest;
    private ((string Path, DateTime Modified, long Size) Key, InstalledServer Info)? _installedCache;

    public UpdateProgress Progress { get; private set; } = UpdateProgress.Idle;

    public event Action? Changed;

    public async Task<VersionManifest> GetManifestAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _manifest is { } cached && DateTimeOffset.Now - cached.FetchedAt < ManifestCacheDuration) return cached;

        await _manifestLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _manifest is { } fresh && DateTimeOffset.Now - fresh.FetchedAt < ManifestCacheDuration) return fresh;

            _manifest = await mojang.GetVersionManifestAsync(cancellationToken);
            return _manifest;
        }
        finally
        {
            _manifestLock.Release();
        }
    }

    /// <summary>Returns download details for a version's dedicated server, or null if it has none (very old versions).</summary>
    public async Task<ServerDownload?> GetServerDownloadAsync(string versionId, CancellationToken cancellationToken = default)
    {
        lock (_downloadCache)
        {
            if (_downloadCache.TryGetValue(versionId, out var cached)) return cached;
        }

        var manifest = await GetManifestAsync(cancellationToken: cancellationToken);
        var version = manifest.Find(versionId) ?? throw new InvalidOperationException($"Minecraft version '{versionId}' was not found.");
        var download = await mojang.GetServerDownloadAsync(version, cancellationToken);

        lock (_downloadCache) _downloadCache[versionId] = download;
        return download;
    }

    /// <summary>Reads version.json from the installed server jar (present in 1.14 and later).</summary>
    public InstalledServer? GetInstalled()
    {
        var jar = new FileInfo(settings.Current.JarPath);
        if (!jar.Exists) return null;
        var key = (jar.FullName, jar.LastWriteTimeUtc, jar.Length);
        if (_installedCache is { } cache && cache.Key == key) return cache.Info;

        string? id = null, name = null;
        int? javaVersion = null;
        try
        {
            using var file = new FileStream(jar.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var zip = new ZipArchive(file, ZipArchiveMode.Read);
            if (zip.GetEntry("version.json") is { } entry)
            {
                using var stream = entry.Open();
                var json = JsonNode.Parse(stream);
                id = (string?)json?["id"];
                name = (string?)json?["name"];
                javaVersion = json?["java_version"] is JsonValue v && v.TryGetValue<int>(out var j) ? j : null;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or FormatException)
        {
            logger.LogWarning(ex, "Could not read version information from {Jar}", jar.FullName);
        }

        var info = new InstalledServer(id, name ?? id ?? "Unknown version", javaVersion, jar.Length, jar.LastWriteTime);
        _installedCache = (key, info);
        return info;
    }

    public async Task<UpdateCheck> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        var installed = GetInstalled();
        var manifest = await GetManifestAsync(cancellationToken: cancellationToken);
        var latestRelease = manifest.Find(manifest.LatestRelease);
        var installedVersion = manifest.Find(installed?.VersionId);

        // Snapshot users are offered whichever of the latest snapshot or release is newer.
        var latest = installedVersion?.Type == "snapshot"
            ? new[] { latestRelease, manifest.Find(manifest.LatestSnapshot) }.OfType<MinecraftVersion>().MaxBy(v => v.ReleaseTime)
            : latestRelease;

        var available = installed is not null && installedVersion is not null && latest is not null
            && latest.ReleaseTime > installedVersion.ReleaseTime;
        return new UpdateCheck(installed, latest, available);
    }

    /// <summary>
    /// Downloads and installs <paramref name="versionId"/>. If the server is running it is backed up (when enabled),
    /// stopped, updated and started again. The previous jar is archived so it can be restored.
    /// </summary>
    public async Task InstallAsync(string versionId, CancellationToken cancellationToken = default)
    {
        if (!await _installLock.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("Another installation is already in progress.");

        var current = settings.Current;
        var tempPath = current.JarPath + ".download";
        SetProgress(new UpdateProgress(true, versionId, "Fetching version details", 0, 0, null, false));
        try
        {
            var download = await GetServerDownloadAsync(versionId, cancellationToken)
                ?? throw new InvalidOperationException($"Minecraft {versionId} has no dedicated server download.");

            if (download.JavaMajorVersion is { } required)
            {
                SetProgress(Progress with { Stage = "Checking Java" });
                var runtime = await java.ProbeAsync(current.JavaPath, cancellationToken);
                if (runtime is null)
                    throw new InvalidOperationException($"Java could not be found at '{current.JavaPath}'. Minecraft {versionId} requires Java {required} or newer. Install it and set the path in Settings.");
                if (runtime.MajorVersion < required)
                    throw new InvalidOperationException($"Minecraft {versionId} requires Java {required} or newer, but the configured Java is version {runtime.MajorVersion}. Install a newer Java and select it in Settings.");
            }

            FolderSecurity.CreatePrivateDirectory(current.ServerDirectory);
            SetProgress(Progress with { Stage = $"Downloading server {versionId}", TotalBytes = download.Size });
            await mojang.DownloadAsync(download, tempPath,
                (received, total) => SetProgress(Progress with { BytesDownloaded = received, TotalBytes = total }), cancellationToken);

            var wasRunning = server.IsRunning;
            var world = properties.LevelName;
            if (current.BackupBeforeUpdate && WorldFolders.GetExistingFolders(current.ServerDirectory, world).Count > 0)
            {
                SetProgress(Progress with { Stage = $"Backing up world '{world}'" });
                await backups.CreateAsync(world, BackupKind.PreUpdate, cancellationToken);
            }

            if (wasRunning)
            {
                SetProgress(Progress with { Stage = "Stopping server" });
                await server.StopServerAsync(cancellationToken);
            }

            SetProgress(Progress with { Stage = "Installing" });
            ArchiveCurrentJar();
            File.Move(tempPath, current.JarPath, overwrite: true);
            _installedCache = null;
            logger.LogInformation("Installed Minecraft server {Version}", versionId);
            server.AddManagerLine($"Installed Minecraft server {versionId}.");

            if (wasRunning)
            {
                SetProgress(Progress with { Stage = "Starting server" });
                await server.StartServerAsync(cancellationToken);
            }

            SetProgress(Progress with { IsRunning = false, Stage = $"Minecraft {versionId} installed", Succeeded = true, FinishedAt = DateTimeOffset.Now });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Installing Minecraft {Version} failed", versionId);
            SetProgress(Progress with { IsRunning = false, Stage = "Failed", Error = ex.Message, FinishedAt = DateTimeOffset.Now });
            throw;
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            _installLock.Release();
        }
    }

    public IReadOnlyList<ArchivedJar> ListArchivedJars()
    {
        var directory = ArchiveDirectory;
        if (!Directory.Exists(directory)) return [];
        return new DirectoryInfo(directory).EnumerateFiles("*.jar")
            .Select(f =>
            {
                // Archived jars are named "{version}__{yyyyMMdd-HHmmss}.jar" (the time they were replaced).
                var separator = f.Name.LastIndexOf("__", StringComparison.Ordinal);
                var version = separator > 0 ? f.Name[..separator] : Path.GetFileNameWithoutExtension(f.Name);
                var archivedAt = separator > 0 && DateTime.TryParseExact(Path.GetFileNameWithoutExtension(f.Name)[(separator + 2)..],
                    ArchiveTimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                    ? parsed
                    : f.LastWriteTime;
                return new ArchivedJar(f.Name, version, archivedAt, f.Length);
            })
            .OrderByDescending(j => j.ArchivedAt)
            .ToList();
    }

    /// <summary>Swaps an archived jar back in (the current jar is archived in turn).</summary>
    public async Task RestoreArchivedJarAsync(string fileName, CancellationToken cancellationToken = default)
    {
        if (Path.GetFileName(fileName) != fileName) throw new ArgumentException("Invalid file name.", nameof(fileName));
        var source = Path.Combine(ArchiveDirectory, fileName);
        if (!File.Exists(source)) throw new FileNotFoundException($"Archived jar '{fileName}' was not found.");
        if (!await _installLock.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("Another installation is already in progress.");

        try
        {
            var current = settings.Current;
            var wasRunning = server.IsRunning;
            if (wasRunning) await server.StopServerAsync(cancellationToken);

            var staging = current.JarPath + ".restore";
            File.Copy(source, staging, overwrite: true);
            ArchiveCurrentJar();
            File.Move(staging, current.JarPath, overwrite: true);
            File.Delete(source);
            _installedCache = null;
            server.AddManagerLine($"Restored server jar from {fileName}.");

            if (wasRunning) await server.StartServerAsync(cancellationToken);
        }
        finally
        {
            _installLock.Release();
            EventHelper.Raise(Changed, logger);
        }
    }

    private string ArchiveDirectory => Path.Combine(settings.Current.ManagerDirectory, "jars");

    private void ArchiveCurrentJar()
    {
        var jarPath = settings.Current.JarPath;
        if (!File.Exists(jarPath)) return;

        var label = WorldFolders.Sanitize(GetInstalled()?.VersionId ?? "unknown");
        Directory.CreateDirectory(ArchiveDirectory);
        var timestamp = DateTime.Now.ToString(ArchiveTimestampFormat, CultureInfo.InvariantCulture);
        File.Move(jarPath, Path.Combine(ArchiveDirectory, $"{label}__{timestamp}.jar"), overwrite: true);
        _installedCache = null;

        foreach (var old in ListArchivedJars().Skip(ArchivedJarsToKeep))
        {
            try { File.Delete(Path.Combine(ArchiveDirectory, old.FileName)); }
            catch (IOException ex) { logger.LogWarning(ex, "Could not delete archived jar {File}", old.FileName); }
        }
    }

    private void SetProgress(UpdateProgress progress)
    {
        Progress = progress;
        EventHelper.Raise(Changed, logger);
    }
}
