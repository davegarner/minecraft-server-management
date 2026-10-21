using System.Text.Json;
using MinecraftServerManager.Util;

namespace MinecraftServerManager.Features.Settings;

/// <summary>
/// Loads and saves <see cref="ManagerSettings"/>. <see cref="Current"/> is an immutable snapshot:
/// all changes go through <see cref="Update"/>, which swaps in a new copy.
/// </summary>
public sealed class SettingsService
{
    private readonly string _filePath;
    private readonly Lock _writeLock = new();
    private readonly ILogger<SettingsService> _logger;
    private ManagerSettings _current;

    public SettingsService(IConfiguration configuration, IHostEnvironment environment, ILogger<SettingsService> logger)
    {
        _logger = logger;
        DataDirectory = ResolveDataDirectory(configuration, environment.ContentRootPath);
        Directory.CreateDirectory(DataDirectory);
        _filePath = Path.Combine(DataDirectory, "settings.json");
        _current = Load();
    }

    public string DataDirectory { get; }

    /// <summary>The current settings. Treat as read-only; use <see cref="Update"/> to change values.</summary>
    public ManagerSettings Current => Volatile.Read(ref _current);

    public event Action? Changed;

    public static string ResolveDataDirectory(IConfiguration configuration, string contentRoot)
    {
        var configured = configuration["Manager:DataDirectory"];
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(contentRoot, "data")
            : Path.GetFullPath(configured, contentRoot);
    }

    public void Update(Action<ManagerSettings> mutate)
    {
        lock (_writeLock)
        {
            var copy = _current.Clone();
            mutate(copy);
            FileUtil.WriteAllTextAtomic(_filePath, JsonSerializer.Serialize(copy, ManagerSettings.JsonOptions));
            Volatile.Write(ref _current, copy);
        }
        EventHelper.Raise(Changed, _logger);
    }

    /// <summary>Returns a user-facing error, or null if the launch and supervision settings in <paramref name="edited"/> are valid.</summary>
    public static string? ValidateServerSettings(ManagerSettings edited)
    {
        if (!Path.IsPathFullyQualified(edited.ServerDirectory.Trim())) return "The server directory must be a full path, e.g. C:\\MinecraftServer.";
        if (!string.IsNullOrWhiteSpace(edited.BackupDirectory) && !Path.IsPathFullyQualified(edited.BackupDirectory.Trim()))
            return "The backup directory must be a full path.";
        var jar = edited.JarFileName.Trim();
        if (jar.Length == 0 || jar != Path.GetFileName(jar) || !jar.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            return "The jar file name must be a plain file name ending in .jar.";
        if (string.IsNullOrWhiteSpace(edited.JavaPath)) return "Enter the path to Java.";
        if (edited.MaxMemoryMb < 512) return "Give the server at least 512 MB of memory.";
        if (edited.MinMemoryMb < 0 || edited.MinMemoryMb > edited.MaxMemoryMb) return "The minimum heap can't be larger than the maximum heap.";
        if (edited.StopTimeoutSeconds is < 5 or > 600) return "The stop timeout must be between 5 and 600 seconds.";
        return null;
    }

    /// <summary>Validates and saves the launch and supervision settings from <paramref name="edited"/>, creating the server directory.</summary>
    /// <exception cref="InvalidOperationException">The settings are invalid or the server directory can't be created.</exception>
    public void SaveServerSettings(ManagerSettings edited)
    {
        if (ValidateServerSettings(edited) is { } error) throw new InvalidOperationException(error);
        try
        {
            Directory.CreateDirectory(edited.ServerDirectory.Trim());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new InvalidOperationException($"Can't create the server directory: {ex.Message}", ex);
        }

        Update(s =>
        {
            s.ServerDirectory = Path.GetFullPath(edited.ServerDirectory.Trim());
            s.JarFileName = edited.JarFileName.Trim();
            s.BackupDirectory = string.IsNullOrWhiteSpace(edited.BackupDirectory) ? "" : Path.GetFullPath(edited.BackupDirectory.Trim());
            s.JavaPath = edited.JavaPath.Trim();
            s.MinMemoryMb = edited.MinMemoryMb;
            s.MaxMemoryMb = edited.MaxMemoryMb;
            s.JvmArguments = edited.JvmArguments.Trim();
            s.ServerArguments = edited.ServerArguments.Trim();
            s.AutoStartServer = edited.AutoStartServer;
            s.AutoRestartOnCrash = edited.AutoRestartOnCrash;
            s.StopTimeoutSeconds = edited.StopTimeoutSeconds;
        });
    }

    private ManagerSettings Load()
    {
        if (!File.Exists(_filePath)) return new ManagerSettings();
        try
        {
            return JsonSerializer.Deserialize<ManagerSettings>(File.ReadAllText(_filePath), ManagerSettings.JsonOptions)
                ?? new ManagerSettings();
        }
        catch (JsonException ex)
        {
            var copy = $"{_filePath}.corrupt-{DateTime.Now:yyyyMMddHHmmss}";
            File.Copy(_filePath, copy, overwrite: true);
            _logger.LogError(ex, "Settings file could not be read; a copy was saved to {Path} and defaults were loaded", copy);
            return new ManagerSettings();
        }
    }
}
