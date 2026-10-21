using System.Text.Json;
using System.Text.Json.Serialization;

namespace MinecraftServerManager.Features.Settings;

/// <summary>Persistent manager configuration, stored as JSON in the data directory.</summary>
public sealed class ManagerSettings
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // Server process
    public string ServerDirectory { get; set; } = OperatingSystem.IsWindows()
        ? @"C:\MinecraftServer"
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "MinecraftServer");
    public string JarFileName { get; set; } = "server.jar";
    public string JavaPath { get; set; } = "java";
    public int MinMemoryMb { get; set; } = 1024;
    public int MaxMemoryMb { get; set; } = 4096;
    public string JvmArguments { get; set; } = "";
    public string ServerArguments { get; set; } = "nogui";
    public bool AutoStartServer { get; set; }
    public bool AutoRestartOnCrash { get; set; } = true;
    public int StopTimeoutSeconds { get; set; } = 60;

    // Backups
    public string BackupDirectory { get; set; } = "";
    public bool ScheduledBackupsEnabled { get; set; }
    public int BackupIntervalMinutes { get; set; } = 360;
    public int BackupRetentionCount { get; set; } = 10;
    public bool BackupOnlyWhenRunning { get; set; } = true;
    public bool AnnounceBackups { get; set; } = true;
    public bool BackupBeforeUpdate { get; set; } = true;
    public DateTimeOffset? LastScheduledBackup { get; set; }

    // Panel account
    public string AdminUsername { get; set; } = "admin";
    public string AdminPasswordHash { get; set; } = "";

    [JsonIgnore] public bool IsSetupComplete => AdminPasswordHash.Length > 0;
    [JsonIgnore] public string JarPath => Path.Combine(ServerDirectory, JarFileName);
    [JsonIgnore] public string PropertiesPath => Path.Combine(ServerDirectory, "server.properties");
    [JsonIgnore] public string EulaPath => Path.Combine(ServerDirectory, "eula.txt");
    [JsonIgnore] public string ManagerDirectory => Path.Combine(ServerDirectory, ".manager");
    [JsonIgnore] public string ResolvedBackupDirectory => string.IsNullOrWhiteSpace(BackupDirectory)
        ? Path.Combine(ServerDirectory, "backups")
        : BackupDirectory;

    public ManagerSettings Clone() =>
        JsonSerializer.Deserialize<ManagerSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;
}
