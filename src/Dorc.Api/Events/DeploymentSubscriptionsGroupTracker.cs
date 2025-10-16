using System.Collections.Concurrent;

namespace Dorc.Api.Events;

/// <summary>
/// Tracks and manages the grouping of connections for deployment subscriptions in order to facilitate targeted event broadcasting.
/// </summary>
/// <remarks>This class provides functionality to register and unregister connections, assign connections to
/// groups, and retrieve connections that are not subscribed to any group. It is designed to handle concurrent access
/// and ensures thread safety when modifying group memberships.</remarks>
internal sealed class DeploymentSubscriptionsGroupTracker : IDeploymentSubscriptionsGroupTracker
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _connectionGroups = new();

    public string GetGroupName(int requestId) => $"req:{requestId}";

    public void RegisterConnection(string connectionId)
        => _connectionGroups.TryAdd(connectionId, new HashSet<string>());

    public void UnregisterConnection(string connectionId)
        => _connectionGroups.TryRemove(connectionId, out _);

    public void JoinGroup(string connectionId, int requestId)
    {
        var set = _connectionGroups.GetOrAdd(connectionId, _ => new HashSet<string>());
        lock (set)
        {
            set.Add(GetGroupName(requestId));
        }
    }

    public void LeaveGroup(string connectionId, int requestId)
    {
        if (_connectionGroups.TryGetValue(connectionId, out var set))
        {
            lock (set)
            {
                set.Remove(GetGroupName(requestId));
            }
        }
    }

    public IReadOnlyList<string> GetUnsubscribedConnections(string? excludeConnectionId = null)
    {
        var result = new List<string>();

        foreach (var kvp in _connectionGroups)
        {
            if (kvp.Key == excludeConnectionId)
                continue;

            var set = kvp.Value;
            lock (set)
            {
                if (set.Count == 0)
                    result.Add(kvp.Key);
            }
        }

        return result;
    }
}
