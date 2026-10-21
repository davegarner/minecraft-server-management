using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using MinecraftServerManager.Util;

namespace MinecraftServerManager.Services;

public enum BackupKind { Manual, Scheduled, PreUpdate, PreRestore, PreDelete }

public sealed record BackupInfo(string FileName, string World, DateTime Created, BackupKind Kind, long SizeBytes)
{
    public string KindLabel => Kind switch
    {
        BackupKind.Manual => "Manual",
        BackupKind.Scheduled => "Scheduled",
        BackupKind.PreUpdate => "Before update",
        BackupKind.PreRestore => "Before restore",
        BackupKind.PreDelete => "Before delete",
        _ => Kind.ToString(),
    };
}

public sealed record BackupProgress(bool IsRunning, string Operation, string? World, string Stage, int FilesDone, int FilesTotal, long BytesDone, long BytesTotal)
{
    public static readonly BackupProgress Idle = new(false, "", null, "", 0, 0, 0, 0);

    public double Percent => BytesTotal > 0 ? Math.Min(100, 100.0 * BytesDone / BytesTotal) : 0;
}

/// <summary>Creates, lists, restores and prunes zip backups of world folders.</summary>
public sealed partial class BackupService(
    SettingsService settings,
    ServerProcessService server,
    ServerPropertiesService properties,
    ILogger<BackupService> logger)
{
    private const string TimestampFormat = "yyyy-MM-dd_HH-mm-ss";
    private static readonly TimeSpan SaveFlushTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);

    private readonly SemaphoreSlim _gate = new(1, 1);

    public BackupProgress Progress { get; private set; } = BackupProgress.Idle;

    public event Action? Changed;

    public string BackupDirectory => settings.Current.ResolvedBackupDirectory;

    public IReadOnlyList<BackupInfo> List()
    {
        var directory = BackupDirectory;
        if (!Directory.Exists(directory)) return [];
        return new DirectoryInfo(directory).EnumerateFiles("*.zip")
            .Select(Describe)
            .OrderByDescending(b => b.Created)
            .ToList();
    }

    /// <summary>Returns the full path of a backup file, or null if the name is invalid or the file doesn't exist.</summary>
    public string? TryResolve(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName
            || !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return null;
        var path = Path.Combine(BackupDirectory, fileName);
        return File.Exists(path) ? path : null;
    }

    public async Task<BackupInfo> CreateAsync(string world, BackupKind kind, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var backup = await CreateCoreAsync(world, kind, cancellationToken);
            if (kind == BackupKind.Scheduled) ApplyRetention(world);
            return backup;
        }
        finally
        {
            SetProgress(BackupProgress.Idle);
            _gate.Release();
        }
    }

    public void Delete(string fileName)
    {
        var path = TryResolve(fileName) ?? throw new FileNotFoundException($"Backup '{fileName}' was not found.");
        File.Delete(path);
        logger.LogInformation("Deleted backup {File}", fileName);
        EventHelper.Raise(Changed, logger);
    }

    /// <summary>
    /// Replaces the world folders contained in the backup. If the backup is of the active world the server
    /// is stopped first and started again afterwards. The current folders are kept until extraction succeeds.
    /// </summary>
    public async Task RestoreAsync(string fileName, bool backupCurrentFirst, CancellationToken cancellationToken = default)
    {
        var path = TryResolve(fileName) ?? throw new FileNotFoundException($"Backup '{fileName}' was not found.");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var serverDirectory = settings.Current.ServerDirectory;
            List<string> topFolders;
            using (var archive = ZipFile.OpenRead(path))
            {
                topFolders = archive.Entries
                    .Select(e => e.FullName.Replace('\\', '/').Split('/')[0])
                    .Where(n => n.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            if (topFolders.Count == 0) throw new InvalidDataException("The backup archive is empty.");
            if (topFolders.Any(f => f is "." or ".." || WorldFolders.ValidateName(f) is not null))
                throw new InvalidDataException("The backup archive contains unexpected top-level entries and can't be restored automatically.");

            var world = topFolders.FirstOrDefault(f => !topFolders.Any(other => other != f && WorldFolders.BelongsTo(f, other))) ?? topFolders[0];
            var activeWorld = properties.LevelName;
            var affectsActive = topFolders.Any(f => WorldFolders.BelongsTo(f, activeWorld));
            var restartAfter = affectsActive && server.IsRunning;

            if (restartAfter)
            {
                SetProgress(new BackupProgress(true, "Restore", world, "Stopping server", 0, 0, 0, 0));
                await server.StopServerAsync(cancellationToken);
            }

            if (backupCurrentFirst && WorldFolders.GetExistingFolders(serverDirectory, world).Count > 0)
                await CreateCoreAsync(world, BackupKind.PreRestore, cancellationToken);

            SetProgress(new BackupProgress(true, "Restore", world, "Extracting backup", 0, 0, 0, 0));
            var suffix = $".restore-old-{DateTime.Now:yyyyMMddHHmmss}";
            var moved = new List<(string Original, string Temporary)>();
            var extractionStarted = false;
            try
            {
                foreach (var folder in topFolders)
                {
                    var current = Path.Combine(serverDirectory, folder);
                    if (!Directory.Exists(current)) continue;
                    Directory.Move(current, current + suffix);
                    moved.Add((current, current + suffix));
                }

                // Every target path is now free, so anything found there after a failure came from the extraction.
                extractionStarted = true;
                await Task.Run(() => ExtractArchive(path, serverDirectory, cancellationToken), cancellationToken);
            }
            catch
            {
                if (extractionStarted)
                    foreach (var folder in topFolders) TryDeleteDirectory(Path.Combine(serverDirectory, folder));
                foreach (var (original, temporary) in moved)
                    if (!Directory.Exists(original)) Directory.Move(temporary, original);
                throw;
            }

            foreach (var (_, temporary) in moved) TryDeleteDirectory(temporary);
            logger.LogInformation("Restored backup {File}", fileName);
            server.AddManagerLine($"Restored world backup {fileName}.");

            if (restartAfter)
            {
                SetProgress(new BackupProgress(true, "Restore", world, "Starting server", 0, 0, 0, 0));
                await server.StartServerAsync(cancellationToken);
            }
        }
        finally
        {
            SetProgress(BackupProgress.Idle);
            _gate.Release();
            EventHelper.Raise(Changed, logger);
        }
    }

    /// <summary>Deletes the oldest scheduled backups of <paramref name="world"/> beyond the retention count.</summary>
    public void ApplyRetention(string world)
    {
        var keep = settings.Current.BackupRetentionCount;
        if (keep <= 0) return;
        var excess = List()
            .Where(b => b.Kind == BackupKind.Scheduled && string.Equals(b.World, world, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(b => b.Created)
            .Skip(keep);
        foreach (var backup in excess)
        {
            try
            {
                File.Delete(Path.Combine(BackupDirectory, backup.FileName));
                logger.LogInformation("Retention: deleted old backup {File}", backup.FileName);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Could not delete old backup {File}", backup.FileName);
            }
        }
        EventHelper.Raise(Changed, logger);
    }

    internal static string BuildFileName(string world, DateTime timestamp, BackupKind kind) =>
        $"{world}_{timestamp.ToString(TimestampFormat, CultureInfo.InvariantCulture)}_{kind.ToString().ToLowerInvariant()}.zip";

    internal static (string World, DateTime Created, BackupKind Kind)? ParseFileName(string fileName)
    {
        var match = FileNameRegex().Match(fileName);
        if (!match.Success) return null;
        if (!DateTime.TryParseExact(match.Groups["ts"].Value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var created))
            return null;
        if (!Enum.TryParse<BackupKind>(match.Groups["kind"].Value, ignoreCase: true, out var kind)) kind = BackupKind.Manual;
        return (match.Groups["world"].Value, created, kind);
    }

    private static BackupInfo Describe(FileInfo file) =>
        ParseFileName(file.Name) is { } parsed
            ? new BackupInfo(file.Name, parsed.World, parsed.Created, parsed.Kind, file.Length)
            : new BackupInfo(file.Name, Path.GetFileNameWithoutExtension(file.Name), file.LastWriteTime, BackupKind.Manual, file.Length);

    private async Task<BackupInfo> CreateCoreAsync(string world, BackupKind kind, CancellationToken cancellationToken)
    {
        var current = settings.Current;
        var folders = WorldFolders.GetExistingFolders(current.ServerDirectory, world);
        if (folders.Count == 0) throw new InvalidOperationException($"World '{world}' was not found in {current.ServerDirectory}.");

        var isLive = server.IsRunning && string.Equals(world, properties.LevelName, StringComparison.OrdinalIgnoreCase);
        if (isLive && server.State != ServerState.Running)
            throw new InvalidOperationException("The server is starting or stopping. Try the backup again in a moment.");

        var backupDirectory = current.ResolvedBackupDirectory;
        Directory.CreateDirectory(backupDirectory);

        SetProgress(new BackupProgress(true, "Backup", world, "Preparing", 0, 0, 0, 0));
        var savingDisabled = false;
        var fileName = BuildFileName(world, DateTime.Now, kind);
        for (var n = 2; File.Exists(Path.Combine(backupDirectory, fileName)); n++)
            fileName = Path.GetFileNameWithoutExtension(BuildFileName(world, DateTime.Now, kind)) + $"_{n}.zip";
        var finalPath = Path.Combine(backupDirectory, fileName);
        var partialPath = finalPath + ".partial";

        try
        {
            if (isLive)
            {
                // Pause autosave and flush everything to disk so the copied region files are consistent.
                SetProgress(Progress with { Stage = "Saving world" });
                if (current.AnnounceBackups) await server.SendCommandAsync("say Starting world backup...");
                await server.SendCommandAsync("save-off");
                savingDisabled = true;
                var flushed = await server.SendCommandAndWaitAsync("save-all flush",
                    line => line.Contains("Saved the game", StringComparison.OrdinalIgnoreCase), SaveFlushTimeout);
                if (!flushed) logger.LogWarning("Timed out waiting for the server to confirm the save; continuing with the backup");
            }

            var files = folders
                .SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                .Where(f => !Path.GetFileName(f).Equals("session.lock", StringComparison.OrdinalIgnoreCase))
                .Select(f => new FileInfo(f))
                .ToList();
            var totalBytes = files.Sum(f => f.Length);

            if (FileUtil.GetFreeSpace(backupDirectory) is { } free && free < totalBytes)
                throw new IOException($"Not enough free disk space for the backup ({Fmt.Bytes(totalBytes)} needed, {Fmt.Bytes(free)} available).");

            SetProgress(Progress with { Stage = "Compressing", FilesTotal = files.Count, BytesTotal = totalBytes });
            var skipped = await Task.Run(() => WriteArchive(partialPath, current.ServerDirectory, files, cancellationToken), cancellationToken);
            File.Move(partialPath, finalPath);

            if (skipped.Count > 0)
                logger.LogWarning("Backup {File} skipped {Count} locked file(s): {Files}", fileName, skipped.Count, string.Join(", ", skipped.Take(10)));
            logger.LogInformation("Created backup {File} ({Size})", fileName, Fmt.Bytes(new FileInfo(finalPath).Length));
            server.AddManagerLine($"Backup created: {fileName}");
        }
        finally
        {
            if (savingDisabled && server.IsRunning)
            {
                try
                {
                    await server.SendCommandAsync("save-on");
                    if (current.AnnounceBackups) await server.SendCommandAsync("say Backup complete.");
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException)
                {
                    logger.LogWarning(ex, "Could not re-enable autosave after the backup");
                }
            }
            if (File.Exists(partialPath)) File.Delete(partialPath);
        }

        EventHelper.Raise(Changed, logger);
        return Describe(new FileInfo(finalPath));
    }

    private List<string> WriteArchive(string archivePath, string serverDirectory, List<FileInfo> files, CancellationToken cancellationToken)
    {
        var skipped = new List<string>();
        var stopwatch = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        long bytesDone = 0;
        var filesDone = 0;

        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entryName = Path.GetRelativePath(serverDirectory, file.FullName).Replace('\\', '/');
            FileStream source;
            try
            {
                // The server keeps region files open; share read/write so we can still copy them.
                source = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped.Add(entryName);
                continue;
            }

            using (source)
            {
                var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                try { entry.LastWriteTime = file.LastWriteTime; }
                catch (ArgumentOutOfRangeException) { /* Timestamps outside the zip range are left at the default. */ }
                using var destination = entry.Open();
                source.CopyTo(destination);
            }

            bytesDone += file.Length;
            filesDone++;
            if (stopwatch.Elapsed - lastReport > ProgressInterval)
            {
                lastReport = stopwatch.Elapsed;
                SetProgress(Progress with { FilesDone = filesDone, BytesDone = bytesDone });
            }
        }
        SetProgress(Progress with { FilesDone = filesDone, BytesDone = bytesDone, Stage = "Finishing" });
        return skipped;
    }

    private static void ExtractArchive(string archivePath, string serverDirectory, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = entry.FullName.Replace('\\', '/');
            if (name.EndsWith('/')) continue;
            var destination = Path.GetFullPath(Path.Combine(serverDirectory, name));
            if (!FileUtil.IsInside(destination, serverDirectory))
                throw new InvalidDataException($"The backup contains an unsafe path: {entry.FullName}");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    private void TryDeleteDirectory(string path)
    {
        try { FileUtil.DeleteDirectory(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not delete {Path}", path);
        }
    }

    private void SetProgress(BackupProgress progress)
    {
        Progress = progress;
        EventHelper.Raise(Changed, logger);
    }

    [GeneratedRegex(@"^(?<world>.+)_(?<ts>\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2})_(?<kind>[a-z]+)(?:_\d+)?\.zip$", RegexOptions.IgnoreCase)]
    private static partial Regex FileNameRegex();
}
