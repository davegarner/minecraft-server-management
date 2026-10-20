using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MinecraftServerManager.Services;

namespace MinecraftServerManager.Tests;

/// <summary>A throwaway data + server directory wired up with the real services (no Java process involved).</summary>
internal sealed class TestServerDirectory : IDisposable
{
    public TestServerDirectory()
    {
        Root = Directory.CreateTempSubdirectory("mcm-tests-").FullName;
        Directory.CreateDirectory(ServerDirectory);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Manager:DataDirectory"] = Path.Combine(Root, "data") })
            .Build();

        Settings = new SettingsService(configuration, new TestHostEnvironment(Root), NullLogger<SettingsService>.Instance);
        Settings.Update(s => s.ServerDirectory = ServerDirectory);
        Properties = new ServerPropertiesService(Settings, NullLogger<ServerPropertiesService>.Instance);
        Server = new ServerProcessService(Settings, Properties, NullLogger<ServerProcessService>.Instance);
        Backups = new BackupService(Settings, Server, Properties, NullLogger<BackupService>.Instance);
        Worlds = new WorldService(Settings, Properties, Server, Backups, NullLogger<WorldService>.Instance);
    }

    public string Root { get; }
    public string ServerDirectory => Path.Combine(Root, "server");
    public SettingsService Settings { get; }
    public ServerPropertiesService Properties { get; }
    public ServerProcessService Server { get; }
    public BackupService Backups { get; }
    public WorldService Worlds { get; }

    /// <summary>Creates a world folder with a real gzipped NBT level.dat and a dummy region file.</summary>
    public string CreateWorld(string name, long seed, int gameType = 0, string version = "1.21.10")
    {
        var path = Path.Combine(ServerDirectory, name);
        Directory.CreateDirectory(Path.Combine(path, "region"));
        File.WriteAllBytes(Path.Combine(path, "region", "r.0.0.mca"), RandomBytes(4096));
        File.WriteAllText(Path.Combine(path, "session.lock"), "lock");
        File.WriteAllBytes(Path.Combine(path, "level.dat"), CreateLevelDat(name, seed, gameType, version));
        return path;
    }

    public static byte[] CreateLevelDat(string name, long seed, int gameType, string version)
    {
        var nbt = new MemoryStream();
        var w = new BinaryWriter(nbt);
        void Name(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            w.Write(BinaryPrimitives.ReverseEndianness((ushort)bytes.Length));
            w.Write(bytes);
        }
        void Tag(byte type, string tagName) { w.Write(type); Name(tagName); }

        Tag(10, "");
        Tag(10, "Data");
        Tag(8, "LevelName"); Name(name);
        Tag(10, "Version"); Tag(8, "Name"); Name(version); w.Write((byte)0);
        Tag(10, "WorldGenSettings"); Tag(4, "seed"); w.Write(BinaryPrimitives.ReverseEndianness(seed)); w.Write((byte)0);
        Tag(3, "GameType"); w.Write(BinaryPrimitives.ReverseEndianness(gameType));
        Tag(1, "hardcore"); w.Write((byte)1);
        Tag(9, "ServerBrands"); w.Write((byte)8); w.Write(BinaryPrimitives.ReverseEndianness(1)); Name("vanilla");
        w.Write((byte)0); // end Data
        w.Write((byte)0); // end root

        var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true)) gzip.Write(nbt.ToArray());
        return output.ToArray();
    }

    public static byte[] RandomBytes(int count)
    {
        var bytes = new byte[count];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }

    public void Dispose()
    {
        Server.Dispose();
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { }
    }

    private sealed class TestHostEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "MinecraftServerManager";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
