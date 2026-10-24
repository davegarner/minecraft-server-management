using System.Text.RegularExpressions;
using MinecraftServerManager.Core;

namespace MinecraftServerManager.Features.Server;

/// <summary>A change to the online players reported in the server log.</summary>
public abstract record PlayerLogEvent
{
    public sealed record Joined(string Name) : PlayerLogEvent;

    public sealed record Left(string Name) : PlayerLogEvent;

    /// <summary>Output of the "list" command: the authoritative set of online players.</summary>
    public sealed record Online(IReadOnlyList<string> Names) : PlayerLogEvent;
}

/// <summary>Recognises the vanilla server log lines the manager reacts to.</summary>
public static partial class ServerLogParser
{
    /// <summary>Removes ANSI colour and cursor escape sequences.</summary>
    public static string StripAnsi(string line) => AnsiEscapeRegex().Replace(line, string.Empty);

    /// <summary>True for the "Done (12.3s)!" line the server prints when it has finished starting.</summary>
    public static bool IsStartupComplete(string line) => DoneRegex().IsMatch(line);

    /// <summary>Returns the player change a line reports, or null if it reports none.</summary>
    public static PlayerLogEvent? ParsePlayerEvent(string line)
    {
        if (JoinRegex().Match(line) is { Success: true } join) return new PlayerLogEvent.Joined(join.Groups["name"].Value);
        if (LeaveRegex().Match(line) is { Success: true } leave) return new PlayerLogEvent.Left(leave.Groups["name"].Value);
        if (ListRegex().Match(line) is { Success: true } list)
        {
            var names = list.Groups["names"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new PlayerLogEvent.Online(names.Where(PlayerName.IsValid).ToArray());
        }
        return null;
    }

    [GeneratedRegex(@"\x1B\[[0-9;?]*[A-Za-z]")]
    private static partial Regex AnsiEscapeRegex();

    [GeneratedRegex(@"\]: Done \([0-9.,]+s\)!")]
    private static partial Regex DoneRegex();

    // Minecraft 26.x prefixes system messages with "System chat: "; older versions don't.
    private const string SystemMessage = @"\]: (?:System chat: )?";

    [GeneratedRegex(SystemMessage + @"(?<name>[A-Za-z0-9_]{1,16})(?: \(formerly known as [A-Za-z0-9_]{1,16}\))? joined the game$")]
    private static partial Regex JoinRegex();

    [GeneratedRegex(SystemMessage + @"(?<name>[A-Za-z0-9_]{1,16}) left the game$")]
    private static partial Regex LeaveRegex();

    [GeneratedRegex(SystemMessage + @"There are \d+ of a max of \d+ players online:(?<names>.*)$")]
    private static partial Regex ListRegex();
}
