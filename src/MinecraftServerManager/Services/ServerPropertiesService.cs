using MinecraftServerManager.Util;

namespace MinecraftServerManager.Services;

/// <summary>Reads and writes server.properties and eula.txt in the server directory.</summary>
public sealed class ServerPropertiesService(SettingsService settings, ILogger<ServerPropertiesService> logger)
{
    public const string EulaUrl = "https://aka.ms/MinecraftEULA";

    private readonly Lock _writeLock = new();

    public event Action? Changed;

    public string FilePath => settings.Current.PropertiesPath;

    public bool FileExists => File.Exists(FilePath);

    public JavaPropertiesDocument Load() => JavaPropertiesDocument.Load(FilePath);

    public string? Get(string key) => Load().Get(key);

    /// <summary>The world folder the server loads (level-name), defaulting to "world".</summary>
    public string LevelName => Get("level-name") is { Length: > 0 } name ? name : "world";

    /// <summary>Sets and removes keys, preserving everything else in the file.</summary>
    public void Update(IReadOnlyDictionary<string, string> values, IEnumerable<string>? removeKeys = null)
    {
        lock (_writeLock)
        {
            var exists = FileExists;
            var doc = Load();
            if (!exists) doc.AddComment("Minecraft server properties (created by Minecraft Server Manager)");
            foreach (var (key, value) in values) doc.Set(key, value);
            foreach (var key in removeKeys ?? Array.Empty<string>()) doc.Remove(key);
            Directory.CreateDirectory(settings.Current.ServerDirectory);
            doc.Save(FilePath);
        }
        logger.LogInformation("Updated server.properties: {Keys}", string.Join(", ", values.Keys));
        EventHelper.Raise(Changed, logger);
    }

    public bool IsEulaAccepted() =>
        string.Equals(JavaPropertiesDocument.Load(settings.Current.EulaPath).Get("eula")?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    public void AcceptEula()
    {
        var current = settings.Current;
        Directory.CreateDirectory(current.ServerDirectory);
        var doc = new JavaPropertiesDocument();
        doc.AddComment($"By changing the setting below to TRUE you are indicating your agreement to our EULA ({EulaUrl}).");
        doc.AddComment($"Accepted via Minecraft Server Manager on {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        doc.Set("eula", "true");
        doc.Save(current.EulaPath);
        logger.LogInformation("Minecraft EULA accepted");
        EventHelper.Raise(Changed, logger);
    }
}
