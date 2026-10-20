using System.Globalization;

namespace MinecraftServerManager.Util;

/// <summary>Display formatting helpers shared by the UI.</summary>
public static class Fmt
{
    private static readonly string[] SizeUnits = ["B", "KB", "MB", "GB", "TB"];

    public static string Bytes(long bytes)
    {
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < SizeUnits.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {SizeUnits[unit]}";
    }

    public static string Duration(TimeSpan span)
    {
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h {span.Minutes}m";
        if (span.TotalHours >= 1) return $"{span.Hours}h {span.Minutes}m";
        if (span.TotalMinutes >= 1) return $"{span.Minutes}m {span.Seconds}s";
        return $"{Math.Max(0, span.Seconds)}s";
    }

    public static string Ago(DateTimeOffset when)
    {
        var delta = DateTimeOffset.Now - when;
        if (delta < TimeSpan.FromMinutes(1)) return "just now";
        if (delta < TimeSpan.FromHours(1)) return $"{(int)delta.TotalMinutes} min ago";
        if (delta < TimeSpan.FromDays(1)) return $"{(int)delta.TotalHours} h ago";
        if (delta < TimeSpan.FromDays(30)) return $"{(int)delta.TotalDays} days ago";
        return Date(when);
    }

    public static string Ago(DateTime when) => Ago(new DateTimeOffset(when));

    public static string Date(DateTimeOffset when) => when.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static string Date(DateTime when) => when.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Removes Minecraft "§" colour/formatting codes, e.g. "§aHello" becomes "Hello".</summary>
    public static string StripFormatting(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '§' && i + 1 < text.Length) i++;
            else sb.Append(text[i]);
        }
        return sb.ToString();
    }

    /// <summary>Percentage formatted for use inside CSS (always invariant culture).</summary>
    public static string CssPercent(double percent) =>
        Math.Clamp(percent, 0, 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";
}
