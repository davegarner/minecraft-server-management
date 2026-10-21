namespace MinecraftServerManager.Util;

/// <summary>
/// Raises events so that one failing subscriber (e.g. a disconnected UI circuit) cannot break the publisher.
/// Subscribers are enumerated without allocating, as this runs for every console line.
/// </summary>
internal static class EventHelper
{
    public static void Raise(Action? handler, ILogger logger)
    {
        if (handler is null) return;
        foreach (var subscriber in Delegate.EnumerateInvocationList(handler))
        {
            try { subscriber(); }
            catch (Exception ex) { logger.LogWarning(ex, "Event subscriber threw an exception"); }
        }
    }

    public static void Raise<T>(Action<T>? handler, T arg, ILogger logger)
    {
        if (handler is null) return;
        foreach (var subscriber in Delegate.EnumerateInvocationList(handler))
        {
            try { subscriber(arg); }
            catch (Exception ex) { logger.LogWarning(ex, "Event subscriber threw an exception"); }
        }
    }
}
