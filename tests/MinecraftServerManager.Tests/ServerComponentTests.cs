using MinecraftServerManager.Features.Server;
using MinecraftServerManager.Features.Settings;

namespace MinecraftServerManager.Tests;

public class ServerComponentTests
{
    private const string Prefix = "[12:00:00] [Server thread/INFO]: ";

    [Fact]
    public void LogParser_RecognisesStartupAndStripsAnsi()
    {
        Assert.True(ServerLogParser.IsStartupComplete(Prefix + "Done (4.521s)! For help, type \"help\""));
        Assert.True(ServerLogParser.IsStartupComplete(Prefix + "Done (4,521s)! For help, type \"help\""));
        Assert.False(ServerLogParser.IsStartupComplete(Prefix + "Preparing spawn area: 83%"));
        Assert.Equal(Prefix + "Done", ServerLogParser.StripAnsi("\u001b[32m" + Prefix + "Done\u001b[0m"));
    }

    [Fact]
    public void LogParser_ParsesJoinsLeavesAndPlayerLists()
    {
        Assert.Equal(new PlayerLogEvent.Joined("Steve"), ServerLogParser.ParsePlayerEvent(Prefix + "Steve joined the game"));
        Assert.Equal(new PlayerLogEvent.Joined("Alex_2"), ServerLogParser.ParsePlayerEvent(Prefix + "Alex_2 (formerly known as Alex) joined the game"));
        Assert.Equal(new PlayerLogEvent.Left("Steve"), ServerLogParser.ParsePlayerEvent(Prefix + "Steve left the game"));

        var list = Assert.IsType<PlayerLogEvent.Online>(
            ServerLogParser.ParsePlayerEvent(Prefix + "There are 3 of a max of 20 players online: Steve, Alex, bad name"));
        Assert.Equal(["Steve", "Alex"], list.Names); // invalid names are dropped
        Assert.Empty(Assert.IsType<PlayerLogEvent.Online>(
            ServerLogParser.ParsePlayerEvent(Prefix + "There are 0 of a max of 20 players online:")).Names);

        // Newer servers (such as 26.3) log system messages with a "System chat: " prefix.
        Assert.Equal(new PlayerLogEvent.Joined("Steve"), ServerLogParser.ParsePlayerEvent(Prefix + "System chat: Steve joined the game"));
        Assert.Equal(new PlayerLogEvent.Left("Steve"), ServerLogParser.ParsePlayerEvent(Prefix + "System chat: Steve left the game"));
        Assert.Equal(["Steve", "Alex"], Assert.IsType<PlayerLogEvent.Online>(
            ServerLogParser.ParsePlayerEvent(Prefix + "System chat: There are 2 of a max of 5 players online: Steve, Alex")).Names);

        Assert.Null(ServerLogParser.ParsePlayerEvent(Prefix + "<Steve> I joined the game"));
        Assert.Null(ServerLogParser.ParsePlayerEvent(Prefix + "<Steve> System chat: Alex joined the game"));
        Assert.Null(ServerLogParser.ParsePlayerEvent(Prefix + "Steve joined the game and left"));
    }

    [Fact]
    public void OnlinePlayerList_KeepsSortedCaseInsensitiveSnapshot()
    {
        var players = new OnlinePlayerList();
        players.Apply(new PlayerLogEvent.Joined("Zed"));
        players.Apply(new PlayerLogEvent.Joined("alex"));
        players.Apply(new PlayerLogEvent.Joined("ALEX"));
        Assert.Equal(["alex", "Zed"], players.Snapshot);

        var before = players.Snapshot;
        players.Apply(new PlayerLogEvent.Left("zed"));
        Assert.Equal(["alex"], players.Snapshot);
        Assert.Equal(["alex", "Zed"], before); // snapshots are immutable

        players.Apply(new PlayerLogEvent.Online(["b", "A"]));
        Assert.Equal(["A", "b"], players.Snapshot);
        players.Clear();
        Assert.Empty(players.Snapshot);
    }

    [Fact]
    public void CrashRestartPolicy_PausesAfterTooManyCrashesInWindow()
    {
        var policy = new CrashRestartPolicy(maxRestarts: 3, window: TimeSpan.FromMinutes(10));
        var start = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

        Assert.True(policy.ShouldRestart(start, out _));
        Assert.True(policy.ShouldRestart(start.AddMinutes(1), out _));
        Assert.True(policy.ShouldRestart(start.AddMinutes(2), out var third));
        Assert.Equal(3, third);
        Assert.False(policy.ShouldRestart(start.AddMinutes(3), out var fourth));
        Assert.Equal(4, fourth);

        // Crashes older than the window no longer count: only the one at +3 min is still inside it.
        Assert.True(policy.ShouldRestart(start.AddMinutes(12.5), out var later));
        Assert.Equal(2, later);
    }

    [Fact]
    public void ConsoleBuffer_KeepsNewestLinesAndHonoursLimits()
    {
        var buffer = new ConsoleBuffer(capacity: 5);
        for (var i = 1; i <= 7; i++) buffer.Append($"line {i}", ConsoleLineKind.Output);

        Assert.Equal(7, buffer.LastId);
        Assert.Equal([3L, 4L, 5L, 6L, 7L], buffer.GetLines(0, int.MaxValue).Select(l => l.Id));
        Assert.Equal([6L, 7L], buffer.GetLines(5, int.MaxValue).Select(l => l.Id));
        Assert.Equal([7L], buffer.GetLines(0, 1).Select(l => l.Id));
        Assert.Empty(buffer.GetLines(7, int.MaxValue));
    }

    [Theory]
    [InlineData("relative/path", "server.jar", 4096, 1024, 60, "The server directory must be a full path, e.g. C:\\MinecraftServer.")]
    [InlineData(null, "../server.jar", 4096, 1024, 60, "The jar file name must be a plain file name ending in .jar.")]
    [InlineData(null, "server.zip", 4096, 1024, 60, "The jar file name must be a plain file name ending in .jar.")]
    [InlineData(null, "server.jar", 256, 128, 60, "Give the server at least 512 MB of memory.")]
    [InlineData(null, "server.jar", 2048, 4096, 60, "The minimum heap can't be larger than the maximum heap.")]
    [InlineData(null, "server.jar", 4096, 1024, 2, "The stop timeout must be between 5 and 600 seconds.")]
    [InlineData(null, "server.jar", 4096, 4096, 600, null)]
    public void ValidateServerSettings_ChecksEachRule(string? directory, string jar, int maxMb, int minMb, int stopTimeout, string? expected)
    {
        var settings = new ManagerSettings
        {
            ServerDirectory = directory ?? Path.GetTempPath(),
            JarFileName = jar,
            MaxMemoryMb = maxMb,
            MinMemoryMb = minMb,
            StopTimeoutSeconds = stopTimeout,
        };
        Assert.Equal(expected, SettingsService.ValidateServerSettings(settings));
    }
}
