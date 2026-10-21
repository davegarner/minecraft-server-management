namespace MinecraftServerManager.Features.Server;

/// <summary>Allows automatic restarts after crashes, but pauses them while the server keeps crashing.</summary>
public sealed class CrashRestartPolicy(int maxRestarts, TimeSpan window)
{
    private readonly List<DateTimeOffset> _crashes = [];

    public TimeSpan Window => window;

    /// <summary>Records a crash at <paramref name="now"/> and decides whether to restart.</summary>
    /// <param name="recentCrashes">How many crashes, including this one, fall within the window.</param>
    public bool ShouldRestart(DateTimeOffset now, out int recentCrashes)
    {
        lock (_crashes)
        {
            _crashes.Add(now);
            _crashes.RemoveAll(t => now - t > window);
            recentCrashes = _crashes.Count;
        }
        return recentCrashes <= maxRestarts;
    }
}
