using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MinecraftServerManager.Features.Java;

public sealed record JavaRuntimeInfo(string Path, int MajorVersion, string Version, string Description);

/// <summary>Detects installed Java runtimes and reports their versions.</summary>
public sealed partial class JavaService(ILogger<JavaService> logger)
{
    public const string DownloadUrl = "https://adoptium.net/temurin/releases/";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, JavaRuntimeInfo? Info)> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Runs <c>java -version</c> for the given executable. Returns null if it can't be run.</summary>
    public async Task<JavaRuntimeInfo?> ProbeAsync(string javaPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(javaPath)) return null;
        if (_cache.TryGetValue(javaPath, out var cached) && DateTimeOffset.Now - cached.At < CacheDuration) return cached.Info;

        var info = await ProbeUncachedAsync(javaPath, cancellationToken);
        _cache[javaPath] = (DateTimeOffset.Now, info);
        return info;
    }

    private async Task<JavaRuntimeInfo?> ProbeUncachedAsync(string javaPath, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(javaPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        psi.ArgumentList.Add("-version");

        Process? process = null;
        try
        {
            process = Process.Start(psi);
            if (process is null) return null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return Parse(javaPath, await stderr + "\n" + await stdout);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or OperationCanceledException or IOException)
        {
            logger.LogDebug(ex, "Could not run {JavaPath} -version", javaPath);
            try { if (process is { HasExited: false }) process.Kill(); }
            catch (Exception killError) when (killError is InvalidOperationException or Win32Exception) { }
            return null;
        }
        finally
        {
            process?.Dispose();
        }
    }

    public static JavaRuntimeInfo? Parse(string path, string output)
    {
        var match = VersionRegex().Match(output);
        if (!match.Success) return null;
        var version = match.Groups[1].Value;
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var description = lines.Length > 1 ? lines[1] : lines[0];
        return new JavaRuntimeInfo(path, ParseMajorVersion(version), version, description);
    }

    /// <summary>Handles both legacy ("1.8.0_381") and modern ("21.0.2") version strings.</summary>
    public static int ParseMajorVersion(string version)
    {
        var parts = version.Split('.', '_', '-', '+');
        if (parts[0] == "1" && parts.Length > 1 && int.TryParse(parts[1], out var legacy)) return legacy;
        return int.TryParse(parts[0], out var major) ? major : 0;
    }

    /// <summary>Searches JAVA_HOME, PATH and common install locations for Java runtimes.</summary>
    public async Task<IReadOnlyList<JavaRuntimeInfo>> DetectAsync(CancellationToken cancellationToken = default)
    {
        var candidates = FindCandidates()
            .Select(p => { try { return Path.GetFullPath(p); } catch { return null; } })
            .Where(p => p is not null && File.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var results = await Task.WhenAll(candidates.Select(p => ProbeAsync(p!, cancellationToken)));
        return results.OfType<JavaRuntimeInfo>()
            .OrderByDescending(j => j.MajorVersion)
            .ThenBy(j => j.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> FindCandidates()
    {
        var exe = OperatingSystem.IsWindows() ? "java.exe" : "java";

        if (Environment.GetEnvironmentVariable("JAVA_HOME") is { Length: > 0 } javaHome)
            yield return Path.Combine(javaHome, "bin", exe);

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate;
            try { candidate = Path.Combine(dir.Trim('"'), exe); }
            catch (ArgumentException) { continue; }
            yield return candidate;
        }

        IEnumerable<string> roots;
        if (OperatingSystem.IsWindows())
        {
            var programFiles = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            };
            string[] vendors =
            [
                "Java", "Eclipse Adoptium", "Eclipse Foundation", "AdoptOpenJDK", "Microsoft", "Zulu", "Azul",
                "Amazon Corretto", "BellSoft", "Semeru", "IBM", "OpenJDK", "Oracle", "RedHat",
            ];
            roots = programFiles.Where(p => p.Length > 0).SelectMany(pf => vendors.Select(v => Path.Combine(pf, v)));
        }
        else if (OperatingSystem.IsMacOS())
        {
            roots = ["/Library/Java/JavaVirtualMachines", "/opt/homebrew/opt", "/usr/local/opt"];
        }
        else
        {
            roots = ["/usr/lib/jvm", "/opt/java"];
        }

        foreach (var root in roots.Where(Directory.Exists))
        {
            string[] subdirectories;
            try { subdirectories = Directory.GetDirectories(root); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            foreach (var sub in subdirectories)
            {
                yield return Path.Combine(sub, "bin", exe);
                yield return Path.Combine(sub, "Contents", "Home", "bin", exe);
                yield return Path.Combine(sub, "libexec", "openjdk.jdk", "Contents", "Home", "bin", exe);
            }
        }
    }

    [GeneratedRegex("version \"([^\"]+)\"")]
    private static partial Regex VersionRegex();
}
