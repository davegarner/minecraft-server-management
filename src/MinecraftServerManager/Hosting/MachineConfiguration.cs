using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MinecraftServerManager.Hosting;

/// <summary>
/// Machine-wide settings in <c>%ProgramData%\Minecraft Server Manager\appsettings.json</c>. The installers write
/// them (panel address and data folder) so they survive upgrades, which replace the files in Program Files.
/// They override the appsettings.json next to the executable, but not environment variables or arguments.
/// </summary>
public static class MachineConfiguration
{
    public const string Command = "configure";
    private const int DefaultPort = 8080;
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Minecraft Server Manager");

    public static string FilePath => Path.Combine(DirectoryPath, "appsettings.json");

    /// <summary>
    /// <c>configure [--port &lt;n&gt;] [--remote-access | --local-only] [--keep-existing]</c>. Updates the given
    /// options, makes sure a data folder is set, and keeps every other key. With <c>--keep-existing</c> an
    /// existing file's address is left alone (used by installers so upgrades don't reset it).
    /// </summary>
    /// <returns>A process exit code: 0 on success, 2 for invalid arguments.</returns>
    public static int Run(string[] args, string? filePath = null, TextWriter? output = null)
    {
        filePath ??= FilePath;
        output ??= Console.Out;

        int? port = null;
        bool? remote = null;
        var keepExisting = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--port" when i + 1 < args.Length
                    && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var p) && p is >= 1 and <= 65535:
                    port = p;
                    i++;
                    break;
                case "--remote-access":
                    remote = true;
                    break;
                case "--local-only":
                    remote = false;
                    break;
                case "--keep-existing":
                    keepExisting = true;
                    break;
                default:
                    output.WriteLine($"Unrecognised or invalid argument: {args[i]}");
                    output.WriteLine("Usage: MinecraftServerManager.exe configure [--port <1-65535>] [--remote-access | --local-only] [--keep-existing]");
                    return 2;
            }
        }

        var exists = File.Exists(filePath);
        var root = exists ? JsonNode.Parse(File.ReadAllText(filePath)) as JsonObject ?? [] : [];

        if (!exists || (!keepExisting && (port is not null || remote is not null)))
        {
            var (currentPort, currentRemote) = ParseUrls((string?)root["Urls"]);
            var newPort = port ?? currentPort ?? DefaultPort;
            var newRemote = remote ?? currentRemote ?? false;
            root["Urls"] = newRemote ? $"http://0.0.0.0:{newPort}" : $"http://localhost:{newPort}";
        }

        if (root["Manager"] is not JsonObject manager)
        {
            manager = [];
            root["Manager"] = manager;
        }
        if (string.IsNullOrWhiteSpace((string?)manager["DataDirectory"]))
            manager["DataDirectory"] = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(filePath))!, "data");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        File.WriteAllText(filePath, root.ToJsonString(WriteOptions));
        output.WriteLine($"Updated {filePath}");
        output.WriteLine($"  Urls: {root["Urls"]}");
        output.WriteLine($"  Data directory: {manager["DataDirectory"]}");
        output.WriteLine("Restart the MinecraftServerManager service for changes to take effect.");
        return 0;
    }

    private static (int? Port, bool? Remote) ParseUrls(string? urls)
    {
        var first = urls?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (first is null) return (null, null);
        // Kestrel accepts wildcard hosts that Uri can't parse, so normalise them first.
        var normalised = first.Replace("://*", "://0.0.0.0").Replace("://+", "://0.0.0.0");
        if (!Uri.TryCreate(normalised, UriKind.Absolute, out var uri)) return (null, null);
        return (uri.Port, !uri.IsLoopback);
    }
}
