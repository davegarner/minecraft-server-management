using System.Globalization;
using System.Text.Json;

namespace MinecraftServerManager.Services;

public enum PropertyKind { Text, Boolean, Integer, Choice, Password, Json }

public sealed record PropertyDefinition(
    string Key,
    string Category,
    PropertyKind Kind,
    string Default,
    string Description,
    string[]? Choices = null,
    long? Min = null,
    long? Max = null);

/// <summary>
/// Metadata for vanilla server.properties keys so the editor can show friendly inputs and descriptions.
/// Keys that aren't listed here (newer versions, mods) are still editable as plain text.
/// </summary>
public static class PropertySchema
{
    public const string OtherCategory = "Other";

    public static readonly string[] Categories =
    [
        "General", "Gameplay", "World", "Players & Security", "Performance", "Resource Pack", "RCON & Query", "Advanced", OtherCategory,
    ];

    public static readonly IReadOnlyList<PropertyDefinition> All =
    [
        // General
        new("motd", "General", PropertyKind.Text, "A Minecraft Server", "Message shown under the server name in the multiplayer server list. Supports § formatting codes."),
        new("server-port", "General", PropertyKind.Integer, "25565", "TCP port the server listens on. Allow it through Windows Firewall so players can connect.", Min: 1, Max: 65535),
        new("server-ip", "General", PropertyKind.Text, "", "Bind to a specific local IP address. Leave blank to listen on all network interfaces."),
        new("max-players", "General", PropertyKind.Integer, "20", "Maximum number of players that can be online at the same time.", Min: 0, Max: int.MaxValue),
        new("enable-status", "General", PropertyKind.Boolean, "true", "Respond to server list pings. When disabled the server appears offline, but players can still join."),
        new("hide-online-players", "General", PropertyKind.Boolean, "false", "Hide the list of online players from server list status responses."),
        new("bug-report-link", "General", PropertyKind.Text, "", "URL shown as the 'Report Server Bugs' link on the disconnect screen."),
        new("accepts-transfers", "General", PropertyKind.Boolean, "false", "Accept players transferred from another server with the /transfer command."),

        // Gameplay
        new("gamemode", "Gameplay", PropertyKind.Choice, "survival", "Default game mode for players joining for the first time.", ["survival", "creative", "adventure", "spectator"]),
        new("force-gamemode", "Gameplay", PropertyKind.Boolean, "false", "Put players into the default game mode every time they join."),
        new("difficulty", "Gameplay", PropertyKind.Choice, "easy", "Difficulty of the world: affects mob damage, hunger and more.", ["peaceful", "easy", "normal", "hard"]),
        new("hardcore", "Gameplay", PropertyKind.Boolean, "false", "Players are switched to spectator mode when they die, and difficulty is locked to hard."),
        new("pvp", "Gameplay", PropertyKind.Boolean, "true", "Allow players to damage each other."),
        new("allow-flight", "Gameplay", PropertyKind.Boolean, "false", "Allow flight in survival mode (e.g. with mods). When false, players airborne for 5 seconds are kicked."),
        new("allow-nether", "Gameplay", PropertyKind.Boolean, "true", "Allow players to travel to the Nether."),
        new("spawn-monsters", "Gameplay", PropertyKind.Boolean, "true", "Spawn hostile mobs."),
        new("spawn-protection", "Gameplay", PropertyKind.Integer, "16", "Radius in blocks around world spawn that only operators can change. 0 disables protection.", Min: 0, Max: int.MaxValue),
        new("enable-command-block", "Gameplay", PropertyKind.Boolean, "false", "Allow command blocks to run commands."),
        new("player-idle-timeout", "Gameplay", PropertyKind.Integer, "0", "Minutes a player can be idle before being kicked. 0 disables.", Min: 0, Max: int.MaxValue),
        new("max-world-size", "Gameplay", PropertyKind.Integer, "29999984", "Maximum radius of the world border, in blocks.", Min: 1, Max: 29999984),

        // World
        new("level-name", "World", PropertyKind.Text, "world", "Folder name of the world the server loads. Use the Worlds page to switch or create worlds."),
        new("level-seed", "World", PropertyKind.Text, "", "Seed used when a new world is generated. Blank means random. Has no effect on existing worlds."),
        new("level-type", "World", PropertyKind.Choice, "minecraft:normal", "World preset used when a new world is generated.",
            ["minecraft:normal", "minecraft:flat", "minecraft:large_biomes", "minecraft:amplified", "minecraft:single_biome_surface"]),
        new("generator-settings", "World", PropertyKind.Json, "{}", "JSON settings used to customise newly generated worlds, such as superflat layers."),
        new("generate-structures", "World", PropertyKind.Boolean, "true", "Generate structures such as villages, temples and strongholds in new chunks."),
        new("initial-enabled-packs", "World", PropertyKind.Text, "vanilla", "Comma-separated data packs enabled when a new world is created."),
        new("initial-disabled-packs", "World", PropertyKind.Text, "", "Comma-separated data packs that are not enabled automatically when a new world is created."),

        // Players & Security
        new("online-mode", "Players & Security", PropertyKind.Boolean, "true", "Verify that players own a Minecraft account. Only disable on private LANs or behind a proxy, because anyone can then join with any name."),
        new("white-list", "Players & Security", PropertyKind.Boolean, "false", "Only allow players on the whitelist to join."),
        new("enforce-whitelist", "Players & Security", PropertyKind.Boolean, "false", "Kick online players who are not on the whitelist when it is reloaded."),
        new("enforce-secure-profile", "Players & Security", PropertyKind.Boolean, "true", "Require players to have a Mojang-signed public key in order to join."),
        new("prevent-proxy-connections", "Players & Security", PropertyKind.Boolean, "false", "Kick players whose connection appears to come through a proxy or VPN."),
        new("op-permission-level", "Players & Security", PropertyKind.Integer, "4", "Default permission level given by /op (1 = bypass spawn protection, 4 = all commands).", Min: 0, Max: 4),
        new("function-permission-level", "Players & Security", PropertyKind.Integer, "2", "Permission level that data pack functions run with.", Min: 1, Max: 4),
        new("broadcast-console-to-ops", "Players & Security", PropertyKind.Boolean, "true", "Show console command output to online operators."),
        new("broadcast-rcon-to-ops", "Players & Security", PropertyKind.Boolean, "true", "Show RCON command output to online operators."),
        new("log-ips", "Players & Security", PropertyKind.Boolean, "true", "Write player IP addresses to the server log when they connect."),

        // Performance
        new("view-distance", "Performance", PropertyKind.Integer, "10", "Radius of chunks (3-32) the server sends to players. Lower values reduce CPU, RAM and bandwidth.", Min: 3, Max: 32),
        new("simulation-distance", "Performance", PropertyKind.Integer, "10", "Radius of chunks (3-32) around players in which mobs, crops and redstone are updated.", Min: 3, Max: 32),
        new("entity-broadcast-range-percentage", "Performance", PropertyKind.Integer, "100", "How close entities must be before they're sent to clients, as a percentage of the default.", Min: 10, Max: 1000),
        new("network-compression-threshold", "Performance", PropertyKind.Integer, "256", "Packets larger than this many bytes are compressed. -1 disables compression.", Min: -1, Max: int.MaxValue),
        new("max-tick-time", "Performance", PropertyKind.Integer, "60000", "Milliseconds a single tick may take before the watchdog shuts the server down. -1 disables the watchdog.", Min: -1, Max: long.MaxValue),
        new("sync-chunk-writes", "Performance", PropertyKind.Boolean, "true", "Write chunk files synchronously. Safer against data loss, but slower."),
        new("region-file-compression", "Performance", PropertyKind.Choice, "deflate", "Compression used for region files.", ["deflate", "lz4", "none"]),
        new("rate-limit", "Performance", PropertyKind.Integer, "0", "Maximum packets per second a player may send before being kicked. 0 disables.", Min: 0, Max: int.MaxValue),
        new("max-chained-neighbor-updates", "Performance", PropertyKind.Integer, "1000000", "Limit on consecutive neighbour updates before further updates are skipped. Negative disables the limit.", Min: int.MinValue, Max: int.MaxValue),
        new("pause-when-empty-seconds", "Performance", PropertyKind.Integer, "60", "Seconds without any players online before the server pauses ticking. 0 or negative disables pausing.", Min: int.MinValue, Max: int.MaxValue),
        new("use-native-transport", "Performance", PropertyKind.Boolean, "true", "Use Linux-optimised networking. Has no effect on Windows."),

        // Resource pack
        new("resource-pack", "Resource Pack", PropertyKind.Text, "", "Direct download URL of a resource pack players are prompted to use."),
        new("resource-pack-sha1", "Resource Pack", PropertyKind.Text, "", "SHA-1 hash of the resource pack, used to verify downloads."),
        new("resource-pack-id", "Resource Pack", PropertyKind.Text, "", "Optional UUID identifying the resource pack."),
        new("resource-pack-prompt", "Resource Pack", PropertyKind.Text, "", "Custom message (JSON text) shown when players are prompted to download the pack."),
        new("require-resource-pack", "Resource Pack", PropertyKind.Boolean, "false", "Disconnect players who decline the resource pack."),

        // RCON & Query
        new("enable-rcon", "RCON & Query", PropertyKind.Boolean, "false", "Enable remote console access over the RCON protocol. Do not expose RCON to the internet."),
        new("rcon.port", "RCON & Query", PropertyKind.Integer, "25575", "TCP port for RCON.", Min: 1, Max: 65535),
        new("rcon.password", "RCON & Query", PropertyKind.Password, "", "Password for RCON. Use a long, random value."),
        new("enable-query", "RCON & Query", PropertyKind.Boolean, "false", "Enable the GameSpy4 query protocol used by server listing sites."),
        new("query.port", "RCON & Query", PropertyKind.Integer, "25565", "UDP port for the query protocol.", Min: 1, Max: 65535),

        // Advanced
        new("enable-jmx-monitoring", "Advanced", PropertyKind.Boolean, "false", "Expose tick time metrics through JMX (requires extra JVM flags)."),
        new("text-filtering-config", "Advanced", PropertyKind.Text, "", "Configuration for an external chat text filtering service."),
        new("text-filtering-version", "Advanced", PropertyKind.Integer, "0", "Version of the text filtering configuration format.", Min: 0, Max: int.MaxValue),
    ];

    private static readonly Dictionary<string, PropertyDefinition> ByKey = All.ToDictionary(p => p.Key, StringComparer.Ordinal);

    public static PropertyDefinition? Find(string key) => ByKey.GetValueOrDefault(key);

    /// <summary>Returns a validation error for <paramref name="value"/>, or null if it is acceptable.</summary>
    public static string? Validate(PropertyDefinition definition, string value)
    {
        switch (definition.Kind)
        {
            case PropertyKind.Integer:
                if (!long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                    return "Must be a whole number.";
                if (definition.Min is { } min && number < min) return $"Must be at least {min}.";
                if (definition.Max is { } max && number > max) return $"Must be at most {max}.";
                return null;
            case PropertyKind.Boolean:
                return value is "true" or "false" ? null : "Must be true or false.";
            case PropertyKind.Json:
                if (string.IsNullOrWhiteSpace(value)) return null;
                try
                {
                    using var _ = JsonDocument.Parse(value);
                    return null;
                }
                catch (JsonException)
                {
                    return "Must be valid JSON.";
                }
            default:
                return null;
        }
    }
}
