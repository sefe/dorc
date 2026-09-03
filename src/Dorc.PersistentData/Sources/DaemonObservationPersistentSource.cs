using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources
{
    public class DaemonObservationPersistentSource : IDaemonObservationPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;

        public DaemonObservationPersistentSource(IDeploymentContextFactory contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public void InsertObservation(int serverId, int daemonId, DateTime observedAt, string? observedStatus, string? errorMessage)
        {
            using var context = _contextFactory.GetContext();
            context.DaemonObservations.Add(new DaemonObservation
            {
                ServerId = serverId,
                DaemonId = daemonId,
                ObservedAt = observedAt,
                ObservedStatus = observedStatus,
                ErrorMessage = errorMessage
            });
            context.SaveChanges();
        }

        public GetDaemonObservationListResponseDto GetObservations(int daemonId, int? serverId, int limit, int page)
        {
            using var context = _contextFactory.GetContext();

            var query = context.DaemonObservations.AsNoTracking().Where(o => o.DaemonId == daemonId);
            if (serverId is int serverIdValue)
                query = query.Where(o => o.ServerId == serverIdValue);

            var totalItems = query.Count();
            var totalPages = limit > 0 ? (int)Math.Ceiling((double)totalItems / limit) : 1;

            // Join to dbo.SERVER to enrich with server name. Using Servers DbSet already on the context.
            var items = (from obs in query
                         join srv in context.Servers on obs.ServerId equals srv.Id into sj
                         from srv in sj.DefaultIfEmpty()
                         orderby obs.ObservedAt descending
                         select new DaemonObservationApiModel
                         {
                             Id = obs.Id,
                             ServerId = obs.ServerId,
                             ServerName = srv != null ? srv.Name : null!,
                             DaemonId = obs.DaemonId,
                             ObservedAt = obs.ObservedAt,
                             ObservedStatus = obs.ObservedStatus,
                             ErrorMessage = obs.ErrorMessage
                         })
                .Skip(Math.Max(0, (page - 1) * limit))
                .Take(Math.Max(1, limit))
                .ToList();

            return new GetDaemonObservationListResponseDto
            {
                CurrentPage = page,
                TotalItems = totalItems,
                TotalPages = totalPages,
                Items = items
            };
        }

        public IDictionary<int, (DateTime ObservedAt, string? Status)> GetLastSeenByDaemon(IEnumerable<int> daemonIds)
        {
            var ids = daemonIds.Distinct().ToList();
            if (ids.Count == 0)
                return new Dictionary<int, (DateTime, string?)>();

            using var context = _contextFactory.GetContext();

            // Group by daemon, pick the row with the max ObservedAt per group.
            var latestPerDaemon = context.DaemonObservations.AsNoTracking()
                .Where(o => ids.Contains(o.DaemonId))
                .GroupBy(o => o.DaemonId)
                .Select(g => new
                {
                    DaemonId = g.Key,
                    ObservedAt = g.Max(o => o.ObservedAt),
                    // Pick the status on the max-dated row. In a tie (same ObservedAt), any is acceptable.
                    Status = g.OrderByDescending(o => o.ObservedAt).Select(o => o.ObservedStatus).FirstOrDefault()
                })
                .ToList();

            return latestPerDaemon.ToDictionary(
                x => x.DaemonId,
                x => (x.ObservedAt, x.Status));
        }
    }
}
