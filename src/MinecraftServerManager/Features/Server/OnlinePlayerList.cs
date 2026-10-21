namespace MinecraftServerManager.Features.Server;

/// <summary>The players currently online, with a sorted snapshot that is rebuilt only when the set changes.</summary>
public sealed class OnlinePlayerList
{
    private readonly HashSet<string> _players = new(StringComparer.OrdinalIgnoreCase);
    private volatile IReadOnlyList<string> _snapshot = [];

    /// <summary>Names sorted case-insensitively; safe to read from any thread.</summary>
    public IReadOnlyList<string> Snapshot => _snapshot;

    public void Apply(PlayerLogEvent change)
    {
        lock (_players)
        {
            switch (change)
            {
                case PlayerLogEvent.Joined joined:
                    _players.Add(joined.Name);
                    break;
                case PlayerLogEvent.Left left:
                    _players.Remove(left.Name);
                    break;
                case PlayerLogEvent.Online online:
                    _players.Clear();
                    _players.UnionWith(online.Names);
                    break;
            }
            Publish();
        }
    }

    public void Clear()
    {
        lock (_players)
        {
            _players.Clear();
            Publish();
        }
    }

    private void Publish() => _snapshot = _players.Order(StringComparer.OrdinalIgnoreCase).ToArray();
}
