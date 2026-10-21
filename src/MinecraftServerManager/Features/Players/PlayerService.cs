using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MinecraftServerManager.Core;
using MinecraftServerManager.Features.Server;
using MinecraftServerManager.Features.ServerProperties;
using MinecraftServerManager.Features.Settings;
using MinecraftServerManager.Infrastructure.Mojang;
using MinecraftServerManager.Util;

namespace MinecraftServerManager.Features.Players;

public sealed record PlayerEntry(string Name, string? Uuid, int? Level = null, string? Reason = null, string? Created = null, string? Expires = null);

public sealed record IpBanEntry(string Ip, string? Reason, string? Created, string? Expires);

/// <summary>
/// Manages operators, the whitelist and bans. While the server is running changes are sent as console commands
/// (the server owns the JSON files and would overwrite direct edits); while stopped the JSON files are edited directly.
/// </summary>
public sealed class PlayerService(
    SettingsService settings,
    ServerProcessService server,
    ServerPropertiesService properties,
    MojangClient mojang,
    ILogger<PlayerService> logger)
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly TimeSpan FileWriteDelay = TimeSpan.FromMilliseconds(750);

    private const string OpsFile = "ops.json";
    private const string WhitelistFile = "whitelist.json";
    private const string BannedPlayersFile = "banned-players.json";
    private const string BannedIpsFile = "banned-ips.json";

    public bool IsWhitelistEnabled => properties.Get(ServerPropertyKeys.WhiteList) == "true";

    /// <summary>The permission level new operators get (op-permission-level, 4 if unset).</summary>
    public string OperatorPermissionLevel => properties.Get(ServerPropertyKeys.OpPermissionLevel) ?? "4";

    /// <summary>Turns the whitelist on or off in server.properties and, if the server is running, applies it immediately.</summary>
    public async Task SetWhitelistEnabledAsync(bool enabled)
    {
        properties.Update(new Dictionary<string, string> { [ServerPropertyKeys.WhiteList] = enabled ? "true" : "false" });
        if (server.State == ServerState.Running) await server.SendCommandAsync(enabled ? "whitelist on" : "whitelist off");
    }

    public IReadOnlyList<PlayerEntry> GetOperators() =>
        ReadArray(OpsFile).Select(n => new PlayerEntry(Str(n, "name") ?? "?", Str(n, "uuid"), Level: n?["level"] is JsonValue level && level.TryGetValue<int>(out var l) ? l : null)).ToList();

    public IReadOnlyList<PlayerEntry> GetWhitelist() =>
        ReadArray(WhitelistFile).Select(n => new PlayerEntry(Str(n, "name") ?? "?", Str(n, "uuid"))).ToList();

    public IReadOnlyList<PlayerEntry> GetBannedPlayers() =>
        ReadArray(BannedPlayersFile)
            .Select(n => new PlayerEntry(Str(n, "name") ?? "?", Str(n, "uuid"), Reason: Str(n, "reason"), Created: Str(n, "created"), Expires: Str(n, "expires")))
            .ToList();

    public IReadOnlyList<IpBanEntry> GetBannedIps() =>
        ReadArray(BannedIpsFile).Select(n => new IpBanEntry(Str(n, "ip") ?? "?", Str(n, "reason"), Str(n, "created"), Str(n, "expires"))).ToList();

    public Task AddOperatorAsync(string name) => ApplyAsync(name, $"op {name}", async () =>
    {
        var (uuid, properName) = await ResolveProfileAsync(name);
        var level = int.TryParse(properties.Get(ServerPropertyKeys.OpPermissionLevel), out var l) ? l : 4;
        Upsert(OpsFile, "uuid", uuid, new JsonObject
        {
            ["uuid"] = uuid, ["name"] = properName, ["level"] = level, ["bypassesPlayerLimit"] = false,
        });
    });

    public Task RemoveOperatorAsync(string name) =>
        ApplyAsync(name, $"deop {name}", () => RemoveWhere(OpsFile, "name", name));

    public Task AddToWhitelistAsync(string name) => ApplyAsync(name, $"whitelist add {name}", async () =>
    {
        var (uuid, properName) = await ResolveProfileAsync(name);
        Upsert(WhitelistFile, "uuid", uuid, new JsonObject { ["uuid"] = uuid, ["name"] = properName });
    });

    public Task RemoveFromWhitelistAsync(string name) =>
        ApplyAsync(name, $"whitelist remove {name}", () => RemoveWhere(WhitelistFile, "name", name));

    public Task BanAsync(string name, string? reason)
    {
        reason = CleanReason(reason);
        return ApplyAsync(name, reason is null ? $"ban {name}" : $"ban {name} {reason}", async () =>
        {
            var (uuid, properName) = await ResolveProfileAsync(name);
            Upsert(BannedPlayersFile, "uuid", uuid, new JsonObject
            {
                ["uuid"] = uuid, ["name"] = properName, ["created"] = Timestamp(), ["source"] = "Server",
                ["expires"] = "forever", ["reason"] = reason ?? "Banned by an operator.",
            });
        });
    }

    public Task PardonAsync(string name) =>
        ApplyAsync(name, $"pardon {name}", () => RemoveWhere(BannedPlayersFile, "name", name));

    public Task BanIpAsync(string ip, string? reason)
    {
        if (!IPAddress.TryParse(ip.Trim(), out var address)) throw new ArgumentException("Enter a valid IP address.");
        var normalized = address.ToString();
        reason = CleanReason(reason);
        return ApplyCommandOrFileAsync(reason is null ? $"ban-ip {normalized}" : $"ban-ip {normalized} {reason}", () =>
        {
            Upsert(BannedIpsFile, "ip", normalized, new JsonObject
            {
                ["ip"] = normalized, ["created"] = Timestamp(), ["source"] = "Server",
                ["expires"] = "forever", ["reason"] = reason ?? "Banned by an operator.",
            });
            return Task.CompletedTask;
        });
    }

    public Task PardonIpAsync(string ip)
    {
        if (!IPAddress.TryParse(ip.Trim(), out var address)) throw new ArgumentException("Enter a valid IP address.");
        var normalized = address.ToString();
        return ApplyCommandOrFileAsync($"pardon-ip {normalized}", () => RemoveWhere(BannedIpsFile, "ip", normalized));
    }

    public async Task KickAsync(string name, string? reason)
    {
        EnsureValidName(name);
        reason = CleanReason(reason);
        await server.SendCommandAsync(reason is null ? $"kick {name}" : $"kick {name} {reason}");
    }

    /// <summary>The UUID the server assigns in offline mode: a v3 UUID of "OfflinePlayer:&lt;name&gt;".</summary>
    public static string OfflineUuid(string name)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        return FormatUuid(Convert.ToHexStringLower(hash));
    }

    private Task ApplyAsync(string name, string command, Func<Task> editFiles)
    {
        EnsureValidName(name);
        return ApplyCommandOrFileAsync(command, editFiles);
    }

    private async Task ApplyCommandOrFileAsync(string command, Func<Task> editFiles)
    {
        if (server.IsRunning)
        {
            await server.SendCommandAsync(command);
            await Task.Delay(FileWriteDelay); // give the server a moment to write the JSON file
            return;
        }
        await editFiles();
    }

    private async Task<(string Uuid, string Name)> ResolveProfileAsync(string name)
    {
        if (string.Equals(properties.Get(ServerPropertyKeys.OnlineMode), "false", StringComparison.OrdinalIgnoreCase))
            return (OfflineUuid(name), name);

        var profile = await mojang.FindProfileAsync(name)
            ?? throw new InvalidOperationException($"No Minecraft account named '{name}' exists.");
        return (FormatUuid(profile.Id), profile.Name);
    }

    private JsonArray ReadArray(string fileName)
    {
        var path = Path.Combine(settings.Current.ServerDirectory, fileName);
        if (!File.Exists(path)) return [];
        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonArray ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            logger.LogWarning(ex, "Could not read {File}", fileName);
            return [];
        }
    }

    private void WriteArray(string fileName, JsonArray array)
    {
        var current = settings.Current;
        Directory.CreateDirectory(current.ServerDirectory);
        FileUtil.WriteAllTextAtomic(Path.Combine(current.ServerDirectory, fileName), array.ToJsonString(WriteOptions));
    }

    private void Upsert(string fileName, string keyProperty, string keyValue, JsonObject entry)
    {
        var array = ReadArray(fileName);
        var existing = array.FirstOrDefault(n => string.Equals(Str(n, keyProperty), keyValue, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) array.Remove(existing);
        array.Add(entry);
        WriteArray(fileName, array);
    }

    private Task RemoveWhere(string fileName, string property, string value)
    {
        var array = ReadArray(fileName);
        foreach (var node in array.Where(n => string.Equals(Str(n, property), value, StringComparison.OrdinalIgnoreCase)).ToList())
            array.Remove(node);
        WriteArray(fileName, array);
        return Task.CompletedTask;
    }

    private static void EnsureValidName(string name)
    {
        if (!PlayerName.IsValid(name)) throw new ArgumentException("Player names are 1-16 characters: letters, numbers and underscores.");
    }

    private static string? CleanReason(string? reason)
    {
        var cleaned = reason?.ReplaceLineEndings(" ").Trim();
        return string.IsNullOrEmpty(cleaned) ? null : cleaned;
    }

    private static string? Str(JsonNode? node, string property) =>
        node?[property] is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    private static string FormatUuid(string hex) =>
        hex.Length == 32 ? $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}" : hex;

    /// <summary>The timestamp format used by the vanilla server in ban lists, e.g. "2024-05-01 10:00:00 +0000".</summary>
    private static string Timestamp()
    {
        var now = DateTimeOffset.Now;
        var offset = now.Offset;
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        return now.ToString("yyyy-MM-dd HH:mm:ss ", CultureInfo.InvariantCulture) + $"{sign}{Math.Abs(offset.Hours):00}{Math.Abs(offset.Minutes):00}";
    }
}
