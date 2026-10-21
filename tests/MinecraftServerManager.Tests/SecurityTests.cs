using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using MinecraftServerManager.Features.Auth;
using MinecraftServerManager.Features.Worlds;
using MinecraftServerManager.Util;

namespace MinecraftServerManager.Tests;

public class SecurityTests : IDisposable
{
    private readonly TestServerDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Theory]
    [InlineData("127.0.0.1", "localhost:8080", true)]
    [InlineData("::1", "[::1]:8080", true)]
    [InlineData("::ffff:127.0.0.1", "127.0.0.1", true)]
    [InlineData("127.0.0.1", "attacker.example:8080", false)] // DNS rebinding: loopback connection, foreign host name
    [InlineData("192.168.1.20", "localhost:8080", false)]     // another computer
    [InlineData("192.168.1.20", "mc-server:8080", false)]
    public void Setup_IsOnlyOfferedToTheServerItself(string remote, string host, bool expected)
    {
        Assert.Equal(expected, AuthService.IsLocalRequest(IPAddress.Parse(remote), new HostString(host)));
        Assert.Equal(expected, Auth().IsSetupAllowed(Context(remote, host)));
        Assert.True(Auth(allowRemoteSetup: true).IsSetupAllowed(Context(remote, host)));
    }

    [Fact]
    public void Lockout_GroupsIPv6ByNetworkAndLocksAfterFiveFailures()
    {
        Assert.Equal("203.0.113.9", AuthService.ClientKey(IPAddress.Parse("203.0.113.9")));
        Assert.Equal("203.0.113.9", AuthService.ClientKey(IPAddress.Parse("::ffff:203.0.113.9")));
        Assert.Equal("2001:db8:1:2::/64", AuthService.ClientKey(IPAddress.Parse("2001:db8:1:2:aaaa:bbbb:cccc:dddd")));
        Assert.Equal(AuthService.ClientKey(IPAddress.Parse("2001:db8:1:2::1")), AuthService.ClientKey(IPAddress.Parse("2001:db8:1:2:ffff::9")));
        Assert.Equal("unknown", AuthService.ClientKey(null));

        var auth = Auth();
        var client = AuthService.ClientKey(IPAddress.Parse("2001:db8:1:2::1"));
        for (var i = 0; i < 4; i++) auth.RegisterFailure(client);
        Assert.False(auth.IsLockedOut(client, out _));
        auth.RegisterFailure(client);
        Assert.True(auth.IsLockedOut(AuthService.ClientKey(IPAddress.Parse("2001:db8:1:2::77")), out var remaining)); // same /64
        Assert.True(remaining > TimeSpan.FromMinutes(4));
        Assert.False(auth.IsLockedOut(AuthService.ClientKey(IPAddress.Parse("2001:db8:1:3::1")), out _));
    }

    [Fact]
    public async Task Extraction_RefusesArchivesLargerThanTheFreeSpace()
    {
        // 4,000 tiny entries that each claim ~4 GB: about 16 TB in total.
        var zip = ZipDeclaringHugeEntries(count: 4000);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _dir.Worlds.ImportWorldAsync(new MemoryStream(zip), "bomb", makeActive: false));
        Assert.Contains("Not enough free disk space", error.Message);
        Assert.False(Directory.Exists(Path.Combine(_dir.ServerDirectory, "bomb")));

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        Assert.Throws<IOException>(() => SafeZip.EnsureSpaceFor(archive.Entries, _dir.Root));
        SafeZip.EnsureSpaceFor(archive.Entries.Where(e => e.Name == "level.dat"), _dir.Root); // a normal archive fits
    }

    [Fact]
    public void LevelDat_ExpandingPastTheLimitIsIgnored()
    {
        var small = Path.Combine(_dir.Root, "small.dat");
        var huge = Path.Combine(_dir.Root, "huge.dat");
        File.WriteAllBytes(small, LevelDatWithPadding(1024 * 1024));
        File.WriteAllBytes(huge, LevelDatWithPadding(40 * 1024 * 1024));

        Assert.Equal("padded", LevelInfo.TryRead(small)?.LevelName);
        Assert.True(new FileInfo(huge).Length < 1024 * 1024); // tiny on disk...
        Assert.Null(LevelInfo.TryRead(huge));                  // ...but refused once it expands past 32 MB
    }

    private AuthService Auth(bool allowRemoteSetup = false) => new(_dir.Settings, new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [AuthService.AllowRemoteSetupKey] = allowRemoteSetup ? "true" : null })
        .Build());

    private static DefaultHttpContext Context(string remote, string host)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        context.Request.Host = new HostString(host);
        return context;
    }

    /// <summary>A world zip with <paramref name="count"/> region files whose central directory claims ~4 GB each.</summary>
    private static byte[] ZipDeclaringHugeEntries(int count)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var level = archive.CreateEntry("w/level.dat").Open()) level.Write(TestServerDirectory.CreateLevelDat("w", 1, 0, "1.21.10"));
            for (var i = 0; i < count; i++)
            {
                using var region = archive.CreateEntry($"w/region/r.{i}.0.mca").Open();
                region.WriteByte(0);
            }
        }

        // Central directory file headers: signature 0x02014b50, uncompressed size at offset 24, name length at 28, name at 46.
        var bytes = stream.ToArray();
        for (var i = 0; i + 46 < bytes.Length; i++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i)) != 0x02014b50) continue;
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i + 28));
            if (Encoding.UTF8.GetString(bytes, i + 46, nameLength).EndsWith(".mca"))
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i + 24), 0xFFFF_FFF0);
        }
        return bytes;
    }

    /// <summary>A gzipped level.dat whose Data compound carries <paramref name="padding"/> zero bytes.</summary>
    private static byte[] LevelDatWithPadding(int padding)
    {
        var nbt = new MemoryStream();
        var w = new BinaryWriter(nbt);
        void Name(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            w.Write(BinaryPrimitives.ReverseEndianness((ushort)bytes.Length));
            w.Write(bytes);
        }
        w.Write((byte)10); Name("");
        w.Write((byte)10); Name("Data");
        w.Write((byte)8); Name("LevelName"); Name("padded");
        w.Write((byte)7); Name("Padding"); w.Write(BinaryPrimitives.ReverseEndianness(padding)); w.Write(new byte[padding]);
        w.Write((byte)0);
        w.Write((byte)0);

        var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true)) gzip.Write(nbt.ToArray());
        return output.ToArray();
    }
}
