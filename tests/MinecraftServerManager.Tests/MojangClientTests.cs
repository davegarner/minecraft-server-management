using System.Net;
using System.Security.Cryptography;
using System.Text;
using MinecraftServerManager.Infrastructure.Mojang;

namespace MinecraftServerManager.Tests;

public class MojangClientTests
{
    [Fact]
    public async Task Manifest_IsOrderedByReleaseTimeNotVersionNumber()
    {
        var client = Client(_ => Json("""
            {
              "latest": { "release": "1.21.10", "snapshot": "26.1-snapshot-1" },
              "versions": [
                { "id": "1.21.10", "type": "release", "url": "https://example/1.21.10.json", "releaseTime": "2025-10-07T09:00:00+00:00" },
                { "id": "26.1-snapshot-1", "type": "snapshot", "url": "https://example/26.json", "releaseTime": "2026-01-10T09:00:00+00:00" },
                { "id": "1.9", "type": "release", "url": "https://example/1.9.json", "releaseTime": "2016-02-29T13:49:54+00:00" }
              ]
            }
            """));

        var manifest = await client.GetVersionManifestAsync();

        Assert.Equal(["26.1-snapshot-1", "1.21.10", "1.9"], manifest.Versions.Select(v => v.Id));
        Assert.Equal("1.21.10", manifest.LatestRelease);
        Assert.Equal("26.1-snapshot-1", manifest.LatestSnapshot);
        Assert.Equal("1.9", manifest.Find("1.9")?.Id);
    }

    [Fact]
    public async Task ServerDownload_ReadsServerJarAndJavaVersion()
    {
        var version = new MinecraftVersion("1.21.10", "release", "https://example/1.21.10.json", DateTimeOffset.Now);
        var client = Client(_ => Json("""
            { "downloads": { "server": { "url": "https://example/server.jar", "sha1": "abc", "size": 123 } }, "javaVersion": { "majorVersion": 21 } }
            """));

        var download = await client.GetServerDownloadAsync(version);

        Assert.Equal(new ServerDownload("1.21.10", "https://example/server.jar", "abc", 123, 21), download);
        Assert.Null(await Client(_ => Json("""{ "downloads": {} }""")).GetServerDownloadAsync(version));
    }

    [Fact]
    public async Task Profile_IsNullForUnknownNames()
    {
        var client = Client(request => request.RequestUri!.AbsolutePath.EndsWith("/Notch")
            ? Json("""{ "id": "069a79f444e94726a5befca90e38aaf5", "name": "Notch" }""")
            : new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Equal(new MojangProfile("069a79f444e94726a5befca90e38aaf5", "Notch"), await client.FindProfileAsync("Notch"));
        Assert.Null(await client.FindProfileAsync("nobody_here"));
    }

    [Fact]
    public async Task Download_VerifiesSha1AndReportsCompletion()
    {
        var content = Encoding.UTF8.GetBytes("fake server jar");
        var sha1 = Convert.ToHexStringLower(SHA1.HashData(content));
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
        var path = Path.Combine(Directory.CreateTempSubdirectory("mcm-mojang-").FullName, "server.jar");
        try
        {
            (long Received, long Total) last = default;
            await client.DownloadAsync(new ServerDownload("1", "https://example/server.jar", sha1, content.Length, null), path,
                (received, total) => last = (received, total));

            Assert.Equal(content, await File.ReadAllBytesAsync(path));
            Assert.Equal((content.Length, content.Length), last);

            await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(
                new ServerDownload("1", "https://example/server.jar", "0000", content.Length, null), path, (_, _) => { }));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    private static MojangClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(new StubHttp(respond));

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler, IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
