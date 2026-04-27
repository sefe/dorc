using Dorc.ApiModel;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IDaemonObservationPersistentSource
    {
        void InsertObservation(int serverId, int daemonId, DateTime observedAt, string? observedStatus, string? errorMessage);

        GetDaemonObservationListResponseDto GetObservations(int daemonId, int? serverId, int limit, int page);

        /// <summary>
        /// Returns the latest observation for each of the supplied daemon IDs.
        /// Used by DaemonsPersistentSource.GetDaemons() to populate LastSeenDate / LastSeenStatus.
        /// </summary>
        IDictionary<int, (DateTime ObservedAt, string? Status)> GetLastSeenByDaemon(IEnumerable<int> daemonIds);
    }
}
