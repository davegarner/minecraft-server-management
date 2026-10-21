using System.Text.RegularExpressions;

namespace MinecraftServerManager.Core;

/// <summary>Java Edition player names: 1-16 letters, digits and underscores.</summary>
public static partial class PlayerName
{
    public static bool IsValid(string? name) => name is not null && NameRegex().IsMatch(name);

    [GeneratedRegex("^[A-Za-z0-9_]{1,16}$")]
    private static partial Regex NameRegex();
}
