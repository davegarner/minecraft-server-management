using MinecraftServerManager.Features.Server;

namespace MinecraftServerManager.Components.Shared;

/// <summary>Maps console lines to CSS classes for colouring.</summary>
public static class ConsoleStyles
{
    public static string ClassFor(ConsoleLine line) => line.Kind switch
    {
        ConsoleLineKind.Manager => "cl-manager",
        ConsoleLineKind.Command => "cl-command",
        ConsoleLineKind.Error => "cl-error",
        _ when line.Text.Contains("/ERROR]", StringComparison.Ordinal) || line.Text.Contains("/FATAL]", StringComparison.Ordinal)
               || line.Text.Contains(" ERROR]", StringComparison.Ordinal) => "cl-error",
        _ when line.Text.Contains("/WARN]", StringComparison.Ordinal) || line.Text.Contains(" WARN]", StringComparison.Ordinal) => "cl-warn",
        _ when line.Text.EndsWith(" joined the game", StringComparison.Ordinal) || line.Text.EndsWith(" left the game", StringComparison.Ordinal) => "cl-join",
        _ when line.Text.Contains("]: <", StringComparison.Ordinal) => "cl-chat",
        _ => "",
    };
}
