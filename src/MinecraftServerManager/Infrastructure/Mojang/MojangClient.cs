using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace MinecraftServerManager.Infrastructure.Mojang;

public sealed record MinecraftVersion(string Id, string Type, string Url, DateTimeOffset ReleaseTime);

public sealed record VersionManifest(string LatestRelease, string LatestSnapshot, IReadOnlyList<MinecraftVersion> Versions, DateTimeOffset FetchedAt)
{
    public MinecraftVersion? Find(string? id) =>
        id is null ? null : Versions.FirstOrDefault(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));
}

public sealed record ServerDownload(string VersionId, string Url, string Sha1, long Size, int? JavaMajorVersion);

/// <summary>A Mojang account; <paramref name="Id"/> is the undashed UUID the API returns.</summary>
public sealed record MojangProfile(string Id, string Name);

/// <summary>
/// Mojang's public APIs: the version manifest, per-version server downloads and player profiles. A client is
/// created per call through <see cref="IHttpClientFactory"/>, so this singleton never pins a stale connection handler.
/// </summary>
public sealed class MojangClient(IHttpClientFactory httpClientFactory)
{
    public const string HttpClientName = "mojang";
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    /// <summary>Configures the named <see cref="HttpClient"/> this class uses.</summary>
    public static void Configure(HttpClient client)
    {
        client.Timeout = TimeSpan.FromMinutes(10);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MinecraftServerManager/1.0");
    }

    /// <summary>Fetches the version manifest, with versions ordered newest first by release time.</summary>
    public async Task<VersionManifest> GetVersionManifestAsync(CancellationToken cancellationToken = default)
    {
        using var client = httpClientFactory.CreateClient(HttpClientName);
        await using var stream = await client.GetStreamAsync(ManifestUrl, cancellationToken);
        var root = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("Empty version manifest.");

        var versions = (root["versions"]?.AsArray() ?? [])
            .Where(v => v is not null)
            .Select(v => new MinecraftVersion(
                (string)v!["id"]!,
                (string?)v["type"] ?? "release",
                (string)v["url"]!,
                DateTimeOffset.Parse((string)v["releaseTime"]!, CultureInfo.InvariantCulture)))
            .OrderByDescending(v => v.ReleaseTime)
            .ToList();

        return new VersionManifest(
            (string?)root["latest"]?["release"] ?? versions.FirstOrDefault(v => v.Type == "release")?.Id ?? "",
            (string?)root["latest"]?["snapshot"] ?? versions.FirstOrDefault()?.Id ?? "",
            versions,
            DateTimeOffset.Now);
    }

    /// <summary>Returns download details for a version's dedicated server, or null if it has none (very old versions).</summary>
    public async Task<ServerDownload?> GetServerDownloadAsync(MinecraftVersion version, CancellationToken cancellationToken = default)
    {
        using var client = httpClientFactory.CreateClient(HttpClientName);
        await using var stream = await client.GetStreamAsync(version.Url, cancellationToken);
        var root = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken);
        var serverNode = root?["downloads"]?["server"];
        return serverNode is null
            ? null
            : new ServerDownload(
                version.Id,
                (string)serverNode["url"]!,
                (string)serverNode["sha1"]!,
                (long?)serverNode["size"] ?? 0,
                (int?)root?["javaVersion"]?["majorVersion"]);
    }

    /// <summary>Looks up an account by name. Returns null if no account has that name.</summary>
    public async Task<MojangProfile?> FindProfileAsync(string name, CancellationToken cancellationToken = default)
    {
        using var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync($"https://api.mojang.com/users/profiles/minecraft/{Uri.EscapeDataString(name)}", cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent) return null;
        response.EnsureSuccessStatusCode();
        var profile = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken)
            ?? throw new InvalidOperationException("Unexpected response from the Mojang API.");
        return new MojangProfile((string)profile["id"]!, (string?)profile["name"] ?? name);
    }

    /// <summary>
    /// Streams a server jar to <paramref name="destination"/> and verifies its SHA-1. <paramref name="onProgress"/>
    /// receives (bytes received, total bytes) at most every 250 ms and once at the end.
    /// </summary>
    public async Task DownloadAsync(ServerDownload download, string destination, Action<long, long> onProgress, CancellationToken cancellationToken = default)
    {
        using var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(download.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? download.Size;

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var buffer = new byte[81920];
        long received = 0;
        var stopwatch = Stopwatch.StartNew();
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            sha1.AppendData(buffer, 0, read);
            received += read;
            if (stopwatch.ElapsedMilliseconds > 250)
            {
                stopwatch.Restart();
                onProgress(received, total);
            }
        }
        onProgress(received, total);

        var hash = Convert.ToHexStringLower(sha1.GetHashAndReset());
        if (!string.Equals(hash, download.Sha1, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The downloaded file failed SHA-1 verification (expected {download.Sha1}, got {hash}).");
    }
}
