using System.Globalization;
using MinecraftServerManager.Core;
using MinecraftServerManager.Features.Auth;
using MinecraftServerManager.Features.Java;
using MinecraftServerManager.Features.Players;
using MinecraftServerManager.Features.Server;
using MinecraftServerManager.Features.ServerProperties;
using MinecraftServerManager.Features.Settings;
using MinecraftServerManager.Util;

namespace MinecraftServerManager.Tests;

public class HelperTests
{
    [Theory]
    [InlineData("openjdk version \"21.0.5\" 2024-10-15 LTS\nOpenJDK Runtime Environment Temurin-21.0.5+11", 21, "21.0.5")]
    [InlineData("java version \"1.8.0_381\"\nJava(TM) SE Runtime Environment (build 1.8.0_381-b09)", 8, "1.8.0_381")]
    [InlineData("openjdk version \"25\" 2025-09-16\nOpenJDK Runtime Environment (build 25+36)", 25, "25")]
    public void JavaService_ParsesVersionOutput(string output, int major, string version)
    {
        var info = JavaService.Parse("java", output);
        Assert.NotNull(info);
        Assert.Equal(major, info.MajorVersion);
        Assert.Equal(version, info.Version);
    }

    [Fact]
    public void JavaService_ReturnsNullForUnrecognisedOutput() =>
        Assert.Null(JavaService.Parse("java", "The operation couldn't be completed. Unable to locate a Java Runtime."));

    [Fact]
    public void CommandLine_SplitsOnWhitespaceAndHonoursQuotes()
    {
        Assert.Equal(["-Xmx4G", "-Dfoo=a b", "nogui"], CommandLine.Split("  -Xmx4G \"-Dfoo=a b\"\tnogui "));
        Assert.Empty(CommandLine.Split("   "));
        Assert.Equal("java \"a b\" c", CommandLine.Join(["java", "a b", "c"]));
    }

    [Fact]
    public void BuildArguments_PutsJvmFlagsBeforeJarAndServerArgsAfter()
    {
        var settings = new ManagerSettings { MinMemoryMb = 1024, MaxMemoryMb = 4096, JvmArguments = "-XX:+UseG1GC", ServerArguments = "nogui" };

        var args = ServerProcessService.BuildArguments(settings);

        Assert.Equal("-Xms1024M", args[0]);
        Assert.Equal("-Xmx4096M", args[1]);
        var jar = args.IndexOf("-jar");
        Assert.True(args.IndexOf("-XX:+UseG1GC") < jar);
        Assert.Equal("server.jar", args[jar + 1]);
        Assert.Equal("nogui", args[^1]);
    }

    [Theory]
    [InlineData("world", null)]
    [InlineData("My World 2.0", null)]
    [InlineData("", "Enter a world name.")]
    [InlineData("con", "'con' is a reserved name on Windows.")]
    [InlineData("backups", "'backups' is used by the server for other files.")]
    [InlineData("../etc", "Use only letters, numbers, spaces, dashes, underscores and dots.")]
    [InlineData("bad/name", "Use only letters, numbers, spaces, dashes, underscores and dots.")]
    [InlineData(".hidden", "World names can't start or end with a space or dot.")]
    public void WorldFolders_ValidatesNames(string name, string? expectedError) =>
        Assert.Equal(expectedError, WorldFolders.ValidateName(name));

    [Fact]
    public void WorldFolders_RecognisesSplitDimensionFolders()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "world", "world_nether", "world_the_end", "lonely_nether" };
        Assert.True(WorldFolders.IsDimensionFolder("world_nether", names));
        Assert.True(WorldFolders.IsDimensionFolder("world_the_end", names));
        Assert.False(WorldFolders.IsDimensionFolder("world", names));
        Assert.False(WorldFolders.IsDimensionFolder("lonely_nether", names));
        Assert.True(WorldFolders.BelongsTo("World_Nether", "world"));
        Assert.Equal("My_World_", WorldFolders.Sanitize("My:World?"));
        Assert.Equal("imported-world", WorldFolders.Sanitize("..."));
    }

    [Fact]
    public void PlayerService_ComputesVanillaOfflineUuids()
    {
        // Matches Java's UUID.nameUUIDFromBytes("OfflinePlayer:Notch").
        Assert.Equal("b50ad385-829d-3141-a216-7e7d7539ba7f", PlayerService.OfflineUuid("Notch"));
        Assert.True(PlayerName.IsValid("Steve_123"));
        Assert.False(PlayerName.IsValid("has space"));
        Assert.False(PlayerName.IsValid("op Steve\nstop"));
        Assert.False(PlayerName.IsValid("seventeen_chars_x"));
    }

    [Fact]
    public void AuthService_HashesAndVerifiesPasswords()
    {
        var hash = AuthService.HashPassword("correct horse battery");

        Assert.StartsWith("pbkdf2-sha256$", hash);
        Assert.True(AuthService.VerifyPassword("correct horse battery", hash));
        Assert.False(AuthService.VerifyPassword("wrong", hash));
        Assert.False(AuthService.VerifyPassword("anything", "not-a-hash"));
        Assert.False(AuthService.VerifyPassword("anything", "pbkdf2-sha256$1000$!!!$???"));
        Assert.NotEqual(hash, AuthService.HashPassword("correct horse battery")); // salted
    }

    [Fact]
    public void PropertySchema_ValidatesByKind()
    {
        var port = PropertySchema.Find("server-port")!;
        Assert.Null(PropertySchema.Validate(port, "25565"));
        Assert.NotNull(PropertySchema.Validate(port, "70000"));
        Assert.NotNull(PropertySchema.Validate(port, "abc"));
        Assert.NotNull(PropertySchema.Validate(PropertySchema.Find("pvp")!, "yes"));
        Assert.NotNull(PropertySchema.Validate(PropertySchema.Find("generator-settings")!, "{not json"));
        Assert.Null(PropertySchema.Validate(PropertySchema.Find("generator-settings")!, "{\"layers\":[]}"));
    }

    [Fact]
    public void Fmt_FormatsInvariantlyAndStripsColourCodes()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("12.5%", Fmt.CssPercent(12.5)); // CSS must never get "12,5%"
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
        Assert.Equal("100%", Fmt.CssPercent(250));
        Assert.Equal("Hello World", Fmt.StripFormatting("§aHello §lWorld"));
        Assert.Equal("1.5 KB", Fmt.Bytes(1536).Replace(',', '.'));
    }
}
