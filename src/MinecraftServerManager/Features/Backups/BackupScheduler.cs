using MinecraftServerManager.Core;
using MinecraftServerManager.Features.Server;
using MinecraftServerManager.Features.ServerProperties;
using MinecraftServerManager.Features.Settings;

namespace MinecraftServerManager.Features.Backups;

/// <summary>Takes a backup of the active world at the configured interval.</summary>
public sealed class BackupScheduler(
    SettingsService settings,
    ServerProcessService server,
    ServerPropertiesService properties,
    BackupService backups,
    ILogger<BackupScheduler> logger) : BackgroundService
{
    /// <summary>When the next scheduled backup is due, or null if scheduling is disabled.</summary>
    public static DateTimeOffset? NextRun(ManagerSettings current)
    {
        if (!current.ScheduledBackupsEnabled) return null;
        return current.LastScheduledBackup is { } last
            ? last + TimeSpan.FromMinutes(Math.Max(5, current.BackupIntervalMinutes))
            : DateTimeOffset.Now;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunIfDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled backup failed");
                server.AddManagerLine($"Scheduled backup failed: {ex.Message}");
            }
        }
    }

    private async Task RunIfDueAsync(CancellationToken stoppingToken)
    {
        var current = settings.Current;
        if (!current.ScheduledBackupsEnabled) return;

        var interval = TimeSpan.FromMinutes(Math.Max(5, current.BackupIntervalMinutes));
        if (current.LastScheduledBackup is { } last && DateTimeOffset.Now - last < interval) return;
        if (current.BackupOnlyWhenRunning && server.State != ServerState.Running) return;

        var world = properties.LevelName;
        if (WorldFolders.GetExistingFolders(current.ServerDirectory, world).Count == 0) return;

        // Record the attempt first so a failing backup isn't retried every minute.
        settings.Update(s => s.LastScheduledBackup = DateTimeOffset.Now);
        logger.LogInformation("Starting scheduled backup of {World}", world);
        await backups.CreateAsync(world, BackupKind.Scheduled, stoppingToken);
    }
}
