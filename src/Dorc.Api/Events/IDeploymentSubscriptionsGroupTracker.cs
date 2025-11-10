namespace Dorc.Api.Events;

/// <summary>
/// Tracks SignalR connections and the request groups they have joined.
/// Allows sending events only to clients subscribed to a request or to those
/// who have not subscribed to any request-specific group.
/// </summary>
public interface IDeploymentSubscriptionsGroupTracker
{
    string GetGroupName(int requestId);
    void RegisterConnection(string connectionId);
    void UnregisterConnection(string connectionId);
    void JoinGroup(string connectionId, int requestId);
    void LeaveGroup(string connectionId, int requestId);
    /// <summary>
    /// Returns connection ids of clients that have not joined any groups.
    /// Optionally exclude a specific connection id (e.g. the caller).
    /// </summary>
    IReadOnlyList<string> GetUnsubscribedConnections(string? excludeConnectionId = null);
    /// <summary>
    /// Returns all group names that a connection has joined.
    /// </summary>
    IReadOnlyList<string> GetGroupsForConnection(string connectionId);
}
