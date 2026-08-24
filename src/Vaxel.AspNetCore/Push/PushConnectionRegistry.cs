using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace Vaxel;

public sealed record PushConnection(
    string ConnectionId,
    string? UserId,
    IReadOnlySet<string> Groups,
    Channel<PushMessage> Channel);

public sealed record PushMessage(string EventName, string Data);

/// <summary>
/// Thread-safe in-process registry of active SSE stream connections.
/// </summary>
public sealed class PushConnectionRegistry
{
    private readonly ConcurrentDictionary<string, PushConnection> _connections = new();
    private readonly VaxelOptions _options;

    public PushConnectionRegistry(IOptions<VaxelOptions> options, IPushBackplane backplane)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(backplane);
        _options = options.Value;

        // Hook backplane to forward messages to local connections
        backplane.Subscribe(DispatchToLocalAsync);
    }

    public bool TryAddConnection(PushConnection connection, out string? error)
    {
        error = null;

        if (connection.UserId is not null)
        {
            var userCount = _connections.Values.Count(c => c.UserId == connection.UserId);
            if (userCount >= _options.Push.MaxConnectionsPerIdentity)
            {
                error = $"Max connection cap ({_options.Push.MaxConnectionsPerIdentity}) exceeded for user.";
                return false;
            }
        }

        return _connections.TryAdd(connection.ConnectionId, connection);
    }

    public void RemoveConnection(string connectionId)
    {
        _connections.TryRemove(connectionId, out _);
    }

    public int TotalConnections => _connections.Count;

    private Task DispatchToLocalAsync(PushScope scope, string eventName, string data)
    {
        var msg = new PushMessage(eventName, data);

        foreach (var conn in _connections.Values)
        {
            if (MatchesScope(conn, scope))
            {
                conn.Channel.Writer.TryWrite(msg);
            }
        }

        return Task.CompletedTask;
    }

    private static bool MatchesScope(PushConnection conn, PushScope scope) => scope switch
    {
        PushScope.BroadcastScope => true,
        PushScope.UserScope user => string.Equals(conn.UserId, user.UserId, StringComparison.Ordinal),
        PushScope.GroupScope group => conn.Groups.Contains(group.GroupId),
        _ => false
    };
}
