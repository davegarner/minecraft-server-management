namespace MinecraftServerManager.Features.Server;

public enum ConsoleLineKind { Output, Error, Command, Manager }

public sealed record ConsoleLine(long Id, string Text, ConsoleLineKind Kind);

/// <summary>
/// Thread-safe ring buffer of the most recent console lines. The line with id N lives at index N % capacity,
/// so readers copy only the lines they ask for.
/// </summary>
public sealed class ConsoleBuffer(int capacity)
{
    private readonly Lock _lock = new();
    private readonly ConsoleLine[] _lines = new ConsoleLine[capacity];
    private long _lastId;

    /// <summary>Id of the newest line, or 0 if there is none. Ids increase by one per line.</summary>
    public long LastId => Volatile.Read(ref _lastId);

    public ConsoleLine Append(string text, ConsoleLineKind kind)
    {
        lock (_lock)
        {
            var line = new ConsoleLine(_lastId + 1, text, kind);
            _lines[(int)(line.Id % _lines.Length)] = line;
            Volatile.Write(ref _lastId, line.Id);
            return line;
        }
    }

    /// <summary>
    /// Returns the newest buffered lines with an id greater than <paramref name="afterId"/>, oldest first and at most
    /// <paramref name="maxCount"/> of them.
    /// </summary>
    public IReadOnlyList<ConsoleLine> GetLines(long afterId, int maxCount)
    {
        lock (_lock)
        {
            var first = Math.Max(Math.Max(afterId, _lastId - _lines.Length) + 1, _lastId - maxCount + 1);
            if (first > _lastId) return [];
            var lines = new ConsoleLine[_lastId - first + 1];
            for (var i = 0; i < lines.Length; i++) lines[i] = _lines[(int)((first + i) % _lines.Length)];
            return lines;
        }
    }
}
