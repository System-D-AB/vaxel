namespace Vaxel;

/// <summary>
/// Defines the recipient scope for server push notifications.
/// </summary>
public abstract record PushScope
{
    private PushScope() { }

    /// <summary>
    /// Targets a specific authenticated user by identifier.
    /// </summary>
    public sealed record UserScope(string UserId) : PushScope;

    /// <summary>
    /// Targets a group/role by identifier.
    /// </summary>
    public sealed record GroupScope(string GroupId) : PushScope;

    /// <summary>
    /// Targets all active push streams.
    /// </summary>
    public sealed record BroadcastScope : PushScope;

    /// <summary>
    /// Targets a specific user.
    /// </summary>
    public static PushScope User(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return new UserScope(userId);
    }

    /// <summary>
    /// Targets a specific group or role.
    /// </summary>
    public static PushScope Group(string groupId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        return new GroupScope(groupId);
    }

    /// <summary>
    /// Targets all connected users.
    /// </summary>
    public static PushScope Broadcast() => new BroadcastScope();
}
