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

    public event Action? ConnectionsChanged;

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

        var added = _connections.TryAdd(connection.ConnectionId, connection);
        if (added)
        {
            ConnectionsChanged?.Invoke();
        }

        return added;
    }

    public void RemoveConnection(string connectionId)
    {
        if (_connections.TryRemove(connectionId, out _))
        {
            ConnectionsChanged?.Invoke();
        }
    }

    public int TotalConnections => _connections.Count;

    public IReadOnlyList<PushConnection> GetConnections(PushScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var list = new List<PushConnection>();
        foreach (var conn in _connections.Values)
        {
            if (MatchesScope(conn, scope))
            {
                list.Add(conn);
            }
        }

        return list;
    }

    public bool HasConnections(PushScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        foreach (var conn in _connections.Values)
        {
            if (MatchesScope(conn, scope))
            {
                return true;
            }
        }

        return false;
    }

    private Task DispatchToLocalAsync(PushScope scope, string eventName, string data)
    {
        if (eventName.StartsWith("vx-bus:", StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

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

    public static bool MatchesScope(PushConnection conn, PushScope scope) => scope switch
    {
        PushScope.BroadcastScope => true,
        PushScope.UserScope user => string.Equals(conn.UserId, user.UserId, StringComparison.Ordinal),
        PushScope.GroupScope group => conn.Groups.Contains(group.GroupId),
        _ => false
    };
}
