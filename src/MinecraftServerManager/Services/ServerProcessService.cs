using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using MinecraftServerManager.Util;

namespace MinecraftServerManager.Services;

public enum ServerState { Stopped, Starting, Running, Stopping, Crashed }

public enum ConsoleLineKind { Output, Error, Command, Manager }

public sealed record ConsoleLine(long Id, string Text, ConsoleLineKind Kind);

/// <summary>
/// Owns the Minecraft server Java process: start/stop/restart, console input and output,
/// online player tracking, crash auto-restart and CPU/memory sampling.
/// </summary>
public sealed partial class ServerProcessService : IHostedService, IDisposable
{
    private const int MaxConsoleLines = 3000;
    private const int MaxAutoRestarts = 3;
    private static readonly TimeSpan AutoRestartWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan AutoRestartDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StatsInterval = TimeSpan.FromSeconds(2);

    private readonly SettingsService _settings;
    private readonly ServerPropertiesService _properties;
    private readonly ILogger<ServerProcessService> _logger;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly SemaphoreSlim _stdin = new(1, 1);
    private readonly Lock _consoleLock = new();
    // Ring buffer: the line with id N lives at index N % MaxConsoleLines, so readers can copy just the lines they need.
    private readonly ConsoleLine[] _console = new ConsoleLine[MaxConsoleLines];
    private readonly HashSet<string> _players = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DateTimeOffset> _recentCrashes = [];
    private readonly Timer _statsTimer;

    private Process? _process;
    private Task _exitMonitor = Task.CompletedTask;
    private IDisposable? _job;
    private volatile bool _stopRequested;
    private volatile IReadOnlyList<string> _onlinePlayers = [];
    private long _lastLineId;
    private TimeSpan _lastCpuTime;
    private DateTimeOffset _lastCpuSample;

    public ServerProcessService(SettingsService settings, ServerPropertiesService properties, ILogger<ServerProcessService> logger)
    {
        _settings = settings;
        _properties = properties;
        _logger = logger;
        _statsTimer = new Timer(_ => SampleStats(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public ServerState State { get; private set; } = ServerState.Stopped;
    public DateTimeOffset? StartedAt { get; private set; }
    public int? LastExitCode { get; private set; }
    public int? ProcessId { get; private set; }
    public double CpuPercent { get; private set; }
    public long MemoryBytes { get; private set; }

    /// <summary>True while a server process exists (starting, running or stopping).</summary>
    public bool IsRunning => State is ServerState.Starting or ServerState.Running or ServerState.Stopping;

    public TimeSpan? Uptime => StartedAt is { } started ? DateTimeOffset.Now - started : null;

    /// <summary>Sorted snapshot, rebuilt only when players join or leave (pages read it on every render).</summary>
    public IReadOnlyList<string> OnlinePlayers => _onlinePlayers;

    /// <summary>Id of the newest console line, or 0 if there is none. Ids increase by one per line.</summary>
    public long LastLineId => Volatile.Read(ref _lastLineId);

    public event Action? StateChanged;
    public event Action? PlayersChanged;
    public event Action? StatsUpdated;
    public event Action<ConsoleLine>? LineReceived;

    /// <summary>
    /// Returns the newest buffered lines with an id greater than <paramref name="afterId"/>, oldest first and at most
    /// <paramref name="maxCount"/> of them. Only the returned lines are copied, so polling for new output stays cheap.
    /// </summary>
    public IReadOnlyList<ConsoleLine> GetConsoleLines(long afterId = 0, int maxCount = MaxConsoleLines)
    {
        lock (_consoleLock)
        {
            var first = Math.Max(Math.Max(afterId, _lastLineId - MaxConsoleLines) + 1, _lastLineId - maxCount + 1);
            if (first > _lastLineId) return [];
            var lines = new ConsoleLine[_lastLineId - first + 1];
            for (var i = 0; i < lines.Length; i++) lines[i] = _console[(int)((first + i) % MaxConsoleLines)];
            return lines;
        }
    }

    public async Task StartServerAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try { StartCore(); }
        finally { _lifecycle.Release(); }
    }

    public async Task StopServerAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try { await StopCoreAsync(); }
        finally { _lifecycle.Release(); }
    }

    public async Task RestartServerAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            AddManagerLine("Restarting server...");
            await StopCoreAsync();
            StartCore();
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>Terminates the process immediately. Anything not yet saved by the server is lost.</summary>
    public void Kill()
    {
        var process = _process;
        if (process is null) return;
        _stopRequested = true;
        AddManagerLine("Force-killing the server process. Changes since the last save may be lost.");
        TryKill(process);
    }

    /// <summary>Sends a console command (no leading slash required).</summary>
    public async Task SendCommandAsync(string command)
    {
        command = command.ReplaceLineEndings(" ").Trim();
        if (command.StartsWith('/')) command = command[1..];
        if (command.Length == 0) return;

        var process = _process ?? throw new InvalidOperationException("The server is not running.");
        if (command.Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            _stopRequested = true;
            SetState(ServerState.Stopping);
        }
        AddLine("> " + command, ConsoleLineKind.Command);
        await WriteLineAsync(process, command);
    }

    /// <summary>Sends a command and waits until an output line satisfies <paramref name="responseMatch"/>.</summary>
    /// <returns>False if no matching line arrived within <paramref name="timeout"/>.</returns>
    public async Task<bool> SendCommandAndWaitAsync(string command, Func<string, bool> responseMatch, TimeSpan timeout)
    {
        var matched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnLine(ConsoleLine line)
        {
            if (line.Kind is ConsoleLineKind.Output or ConsoleLineKind.Error && responseMatch(line.Text))
                matched.TrySetResult();
        }

        LineReceived += OnLine;
        try
        {
            await SendCommandAsync(command);
            return await Task.WhenAny(matched.Task, Task.Delay(timeout)) == matched.Task;
        }
        finally
        {
            LineReceived -= OnLine;
        }
    }

    public void AddManagerLine(string text) =>
        AddLine($"[{DateTime.Now:HH:mm:ss}] [Manager] {text}", ConsoleLineKind.Manager);

    internal static List<string> BuildArguments(ManagerSettings settings)
    {
        var args = new List<string>();
        if (settings.MinMemoryMb > 0) args.Add($"-Xms{settings.MinMemoryMb}M");
        if (settings.MaxMemoryMb > 0) args.Add($"-Xmx{settings.MaxMemoryMb}M");
        // Make console output UTF-8 regardless of the Windows code page (ignored by Java versions before 19).
        args.Add("-Dstdout.encoding=UTF-8");
        args.Add("-Dstderr.encoding=UTF-8");
        args.AddRange(CommandLine.Split(settings.JvmArguments));
        args.Add("-jar");
        args.Add(settings.JarFileName);
        args.AddRange(CommandLine.Split(settings.ServerArguments));
        return args;
    }

    private void StartCore()
    {
        if (_process is not null) throw new InvalidOperationException("The server is already running.");

        var settings = _settings.Current;
        if (!Directory.Exists(settings.ServerDirectory))
            throw new InvalidOperationException($"The server directory '{settings.ServerDirectory}' does not exist. Check the path in Settings.");
        if (!File.Exists(settings.JarPath))
            throw new InvalidOperationException($"No server jar was found at '{settings.JarPath}'. Install a Minecraft version from the Updates page.");
        if (!_properties.IsEulaAccepted())
            throw new InvalidOperationException("The Minecraft EULA has not been accepted. Accept it on the Dashboard before starting the server.");

        var psi = new ProcessStartInfo
        {
            FileName = settings.JavaPath,
            WorkingDirectory = settings.ServerDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        foreach (var arg in BuildArguments(settings)) psi.ArgumentList.Add(arg);

        AddManagerLine($"Starting server: {settings.JavaPath} {CommandLine.Join(psi.ArgumentList)}");
        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("The Java process could not be started.");
        }
        catch (Win32Exception ex)
        {
            AddManagerLine($"Failed to launch Java: {ex.Message}");
            throw new InvalidOperationException($"Could not launch Java using '{settings.JavaPath}': {ex.Message}. Set the Java path in Settings.", ex);
        }

        if (OperatingSystem.IsWindows()) _job = WindowsJobObject.TryAttach(process, _logger);

        _stopRequested = false;
        lock (_players)
        {
            _players.Clear();
            _onlinePlayers = [];
        }
        _process = process;
        ProcessId = process.Id;
        StartedAt = DateTimeOffset.Now;
        LastExitCode = null;
        _lastCpuTime = TimeSpan.Zero;
        _lastCpuSample = DateTimeOffset.Now;
        SetState(ServerState.Starting);
        _logger.LogInformation("Minecraft server started (PID {Pid})", process.Id);

        var stdout = Task.Run(() => PumpAsync(process.StandardOutput, ConsoleLineKind.Output));
        var stderr = Task.Run(() => PumpAsync(process.StandardError, ConsoleLineKind.Error));
        _exitMonitor = MonitorExitAsync(process, stdout, stderr);
        _statsTimer.Change(StatsInterval, StatsInterval);
    }

    private async Task StopCoreAsync()
    {
        var process = _process;
        if (process is null)
        {
            if (State == ServerState.Crashed) SetState(ServerState.Stopped);
            return;
        }

        _stopRequested = true;
        SetState(ServerState.Stopping);
        AddManagerLine("Stopping server...");
        try
        {
            await WriteLineAsync(process, "stop");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            _logger.LogWarning(ex, "Could not send the stop command");
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(5, _settings.Current.StopTimeoutSeconds));
        var monitor = _exitMonitor;
        if (await Task.WhenAny(monitor, Task.Delay(timeout)) != monitor)
        {
            AddManagerLine($"Server did not stop within {timeout.TotalSeconds:0} seconds; killing the process.");
            TryKill(process);
            await Task.WhenAny(monitor, Task.Delay(TimeSpan.FromSeconds(15)));
        }
    }

    private async Task MonitorExitAsync(Process process, Task stdout, Task stderr)
    {
        try
        {
            await process.WaitForExitAsync();
            await Task.WhenAny(Task.WhenAll(stdout, stderr), Task.Delay(TimeSpan.FromSeconds(5)));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while waiting for the server process to exit");
        }
        HandleExit(process);
    }

    private void HandleExit(Process process)
    {
        int exitCode;
        try { exitCode = process.ExitCode; }
        catch (InvalidOperationException) { exitCode = -1; }

        var previous = State;
        // A clean exit after the server finished starting (e.g. an operator typed /stop in-game) is not a crash.
        var crashed = !_stopRequested && (previous == ServerState.Starting || exitCode != 0);

        _statsTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _process = null;
        _job?.Dispose();
        _job = null;
        process.Dispose();
        ProcessId = null;
        StartedAt = null;
        CpuPercent = 0;
        MemoryBytes = 0;
        LastExitCode = exitCode;
        if (_onlinePlayers.Count > 0) UpdatePlayers(players => players.Clear());

        AddManagerLine(crashed
            ? $"Server process exited unexpectedly (exit code {exitCode})."
            : $"Server stopped (exit code {exitCode}).");
        _logger.Log(crashed ? LogLevel.Warning : LogLevel.Information, "Minecraft server exited with code {ExitCode}", exitCode);
        SetState(crashed ? ServerState.Crashed : ServerState.Stopped);

        if (crashed && previous == ServerState.Running && _settings.Current.AutoRestartOnCrash)
            _ = AutoRestartAsync();
    }

    private async Task AutoRestartAsync()
    {
        int recent;
        lock (_recentCrashes)
        {
            var now = DateTimeOffset.Now;
            _recentCrashes.Add(now);
            _recentCrashes.RemoveAll(t => now - t > AutoRestartWindow);
            recent = _recentCrashes.Count;
        }
        if (recent > MaxAutoRestarts)
        {
            AddManagerLine($"The server crashed {recent} times within {AutoRestartWindow.TotalMinutes:0} minutes. Automatic restart is paused; start it manually after fixing the problem.");
            return;
        }

        AddManagerLine($"Restarting automatically in {AutoRestartDelay.TotalSeconds:0} seconds...");
        await Task.Delay(AutoRestartDelay);
        if (State != ServerState.Crashed) return; // Someone started or stopped it in the meantime.
        try
        {
            await StartServerAsync();
        }
        catch (Exception ex)
        {
            AddManagerLine($"Automatic restart failed: {ex.Message}");
            _logger.LogError(ex, "Automatic restart failed");
        }
    }

    private async Task PumpAsync(StreamReader reader, ConsoleLineKind kind)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line) HandleOutputLine(line, kind);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The stream closes when the process exits.
        }
    }

    private void HandleOutputLine(string raw, ConsoleLineKind kind)
    {
        var text = AnsiEscapeRegex().Replace(raw, string.Empty);
        AddLine(text, kind);

        if (State == ServerState.Starting && DoneRegex().IsMatch(text))
        {
            SetState(ServerState.Running);
            return;
        }

        if (JoinRegex().Match(text) is { Success: true } join)
        {
            var name = join.Groups["name"].Value;
            UpdatePlayers(players => players.Add(name));
        }
        else if (LeaveRegex().Match(text) is { Success: true } leave)
        {
            var name = leave.Groups["name"].Value;
            UpdatePlayers(players => players.Remove(name));
        }
        else if (ListRegex().Match(text) is { Success: true } list)
        {
            // Output of the "list" command: resynchronise with the authoritative player list.
            var names = list.Groups["names"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            UpdatePlayers(players =>
            {
                players.Clear();
                foreach (var name in names)
                    if (PlayerService.IsValidName(name)) players.Add(name);
            });
        }
    }

    private void UpdatePlayers(Action<HashSet<string>> change)
    {
        lock (_players)
        {
            change(_players);
            _onlinePlayers = _players.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        EventHelper.Raise(PlayersChanged, _logger);
    }

    private ConsoleLine AddLine(string text, ConsoleLineKind kind)
    {
        ConsoleLine line;
        lock (_consoleLock)
        {
            line = new ConsoleLine(_lastLineId + 1, text, kind);
            _console[(int)(line.Id % MaxConsoleLines)] = line;
            Volatile.Write(ref _lastLineId, line.Id);
        }
        EventHelper.Raise(LineReceived, line, _logger);
        return line;
    }

    private void SetState(ServerState state)
    {
        if (State == state) return;
        State = state;
        EventHelper.Raise(StateChanged, _logger);
    }

    private async Task WriteLineAsync(Process process, string text)
    {
        await _stdin.WaitAsync();
        try
        {
            await process.StandardInput.WriteLineAsync(text);
            await process.StandardInput.FlushAsync();
        }
        finally
        {
            _stdin.Release();
        }
    }

    private void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            _logger.LogDebug(ex, "Kill failed (process probably already exited)");
        }
    }

    private void SampleStats()
    {
        var process = _process;
        if (process is null) return;
        try
        {
            process.Refresh();
            var now = DateTimeOffset.Now;
            var cpu = process.TotalProcessorTime;
            var elapsedMs = (now - _lastCpuSample).TotalMilliseconds;
            if (_lastCpuTime > TimeSpan.Zero && elapsedMs > 0)
                CpuPercent = Math.Clamp((cpu - _lastCpuTime).TotalMilliseconds / (elapsedMs * Environment.ProcessorCount) * 100, 0, 100);
            _lastCpuTime = cpu;
            _lastCpuSample = now;
            MemoryBytes = process.WorkingSet64;
            EventHelper.Raise(StatsUpdated, _logger);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Process exited between samples.
        }
    }

    Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        if (_settings.Current.AutoStartServer)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    AddManagerLine("Auto-start is enabled; starting the server.");
                    await StartServerAsync();
                }
                catch (Exception ex)
                {
                    AddManagerLine($"Auto-start failed: {ex.Message}");
                    _logger.LogWarning(ex, "Auto-start failed");
                }
            }, CancellationToken.None);
        }
        return Task.CompletedTask;
    }

    async Task IHostedService.StopAsync(CancellationToken cancellationToken)
    {
        if (_process is null) return;
        _logger.LogInformation("Manager is shutting down; stopping the Minecraft server gracefully");
        await StopServerAsync(cancellationToken);
    }

    public void Dispose()
    {
        _statsTimer.Dispose();
        if (_process is { } process) TryKill(process);
        _job?.Dispose();
    }

    [GeneratedRegex(@"\x1B\[[0-9;?]*[A-Za-z]")]
    private static partial Regex AnsiEscapeRegex();

    [GeneratedRegex(@"\]: Done \([0-9.,]+s\)!")]
    private static partial Regex DoneRegex();

    [GeneratedRegex(@"\]: (?<name>[A-Za-z0-9_]{1,16})(?: \(formerly known as [A-Za-z0-9_]{1,16}\))? joined the game$")]
    private static partial Regex JoinRegex();

    [GeneratedRegex(@"\]: (?<name>[A-Za-z0-9_]{1,16}) left the game$")]
    private static partial Regex LeaveRegex();

    [GeneratedRegex(@"\]: There are \d+ of a max of \d+ players online:(?<names>.*)$")]
    private static partial Regex ListRegex();
}
