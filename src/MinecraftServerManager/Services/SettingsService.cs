using System.Text.Json;
using MinecraftServerManager.Util;

namespace MinecraftServerManager.Services;

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
