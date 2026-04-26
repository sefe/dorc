using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Extensions;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Dorc.PersistentData.Sources
{
    public class DaemonAuditPersistentSource : IDaemonAuditPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;

        public DaemonAuditPersistentSource(IDeploymentContextFactory contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public void InsertDaemonAudit(string username, ActionType action, int? daemonId, string? fromValue, string? toValue)
        {
            // Skip no-op Updates (matching ScriptsAuditPersistentSource.AddRecord convention)
            if (action == ActionType.Update && string.Equals(fromValue, toValue))
            {
                return;
            }

            using (var context = _contextFactory.GetContext())
            {
                var actionRow = context.RefDataAuditActions.First(x => x.Action == action);

                var audit = new DaemonAudit
                {
                    Date = DateTime.Now,
                    Username = username,
                    DaemonId = daemonId,
                    RefDataAuditActionId = actionRow.RefDataAuditActionId,
                    Action = actionRow,
                    FromValue = fromValue,
                    ToValue = toValue
                };

                context.DaemonAudits.Add(audit);
                context.SaveChanges();
            }
        }

        public GetDaemonAuditListResponseDto GetDaemonAuditByDaemonId(int daemonId, int limit, int page, PagedDataOperators operators)
        {
            using (var context = _contextFactory.GetContext())
            {
                var queryable = context.DaemonAudits
                    .Include(a => a.Action)
                    .Where(a => a.DaemonId == daemonId);

                // DaemonAudit.DaemonId is int? (nullable), which the string-based
                // ContainsExpression helper doesn't support — a ContainsExpression call there
                // would return null and crash WhereAll at runtime. Apply the daemon filter
                // explicitly above and keep the user-supplied filters below.
                return RunPagedQuery(context, queryable, limit, page, operators);
            }
        }

        public GetDaemonAuditListResponseDto GetDaemonAudit(int limit, int page, PagedDataOperators operators)
        {
            using (var context = _contextFactory.GetContext())
            {
                var queryable = context.DaemonAudits
                    .Include(a => a.Action)
                    .AsQueryable();

                return RunPagedQuery(context, queryable, limit, page, operators);
            }
        }

        // Shared filter/sort/page/project pipeline for the per-record and cross-record audit
        // queries. The DaemonName join is resolved post-page via a dictionary lookup against
        // the paged Ids — there is no FK between DaemonAudit.DaemonId and Daemon.Id (intentional,
        // so audit history survives daemon deletion) and the EF model has no navigation property,
        // so a single round-trip after pagination is cheaper than a per-row join in SQL.
        private static GetDaemonAuditListResponseDto RunPagedQuery(
            IDeploymentContext context,
            IQueryable<DaemonAudit> queryable,
            int limit, int page, PagedDataOperators operators)
        {
            PagedModel<DaemonAudit> output = null;

            var filterLambdas = new List<Expression<Func<DaemonAudit, bool>>>();

            if (operators.Filters != null && operators.Filters.Any())
            {
                foreach (var pagedDataFilter in operators.Filters)
                {
                    if (pagedDataFilter == null) continue;
                    if (!string.IsNullOrEmpty(pagedDataFilter.Path) && !string.IsNullOrEmpty(pagedDataFilter.FilterValue))
                    {
                        // ContainsExpression returns null for property types other than string/int
                        // (e.g. DateTime, bool); skip those rather than feeding null into WhereAll.
                        var expr = queryable.ContainsExpression(pagedDataFilter.Path, pagedDataFilter.FilterValue);
                        if (expr != null)
                        {
                            filterLambdas.Add(expr);
                        }
                    }
                }
            }

            // WhereAll treats an empty predicate list as "match nothing" (x => false), so
            // only apply it when we actually have user-supplied filters.
            if (filterLambdas.Count > 0)
            {
                queryable = WhereAll(queryable, filterLambdas.ToArray());
            }

            if (operators.SortOrders != null && operators.SortOrders.Any())
            {
                IOrderedQueryable<DaemonAudit> orderedQuery = null;

                for (var i = 0; i < operators.SortOrders.Count; i++)
                {
                    if (operators.SortOrders[i] == null) continue;
                    if (string.IsNullOrEmpty(operators.SortOrders[i].Path) ||
                        string.IsNullOrEmpty(operators.SortOrders[i].Direction))
                        continue;

                    var param = Expression.Parameter(typeof(DaemonAudit), "DaemonAudit");
                    var prop = Expression.PropertyOrField(param, operators.SortOrders[i].Path);

                    switch (prop.Type)
                    {
                        case Type boolType when boolType == typeof(bool):
                            orderedQuery = OrderEntries(operators, i, orderedQuery, queryable,
                                GetExpressionForOrdering<bool>(prop, param));
                            break;
                        case Type stringType when stringType == typeof(string):
                            orderedQuery = OrderEntries(operators, i, orderedQuery, queryable,
                                GetExpressionForOrdering<string>(prop, param));
                            break;
                        case Type intType when intType == typeof(int):
                            orderedQuery = OrderEntries(operators, i, orderedQuery, queryable,
                                GetExpressionForOrdering<int>(prop, param));
                            break;
                        case Type datetimeType when datetimeType == typeof(DateTime):
                            orderedQuery = OrderEntries(operators, i, orderedQuery, queryable,
                                GetExpressionForOrdering<DateTime>(prop, param));
                            break;
                    }
                }

                if (orderedQuery != null)
                    output = orderedQuery.AsNoTracking().Paginate(page, limit);
            }

            if (output == null)
                output = queryable.AsNoTracking().OrderByDescending(a => a.Date).Paginate(page, limit);

            var pagedDaemonIds = output.Items
                .Where(a => a.DaemonId.HasValue)
                .Select(a => a.DaemonId!.Value)
                .Distinct()
                .ToList();

            var daemonNamesById = pagedDaemonIds.Count == 0
                ? new Dictionary<int, string>()
                : context.Daemons
                    .Where(d => pagedDaemonIds.Contains(d.Id))
                    .AsNoTracking()
                    .ToDictionary(d => d.Id, d => d.Name);

            return new GetDaemonAuditListResponseDto
            {
                CurrentPage = output.CurrentPage,
                TotalPages = output.TotalPages,
                TotalItems = output.TotalItems,
                Items = output.Items.Select(a => new DaemonAuditApiModel
                {
                    Id = a.Id,
                    DaemonId = a.DaemonId,
                    DaemonName = a.DaemonId.HasValue && daemonNamesById.TryGetValue(a.DaemonId.Value, out var name)
                        ? name
                        : null,
                    RefDataAuditActionId = a.RefDataAuditActionId,
                    Action = a.Action.Action.ToString(),
                    Username = a.Username,
                    Date = a.Date,
                    FromValue = a.FromValue,
                    ToValue = a.ToValue
                }).ToList()
            };
        }

        private static IOrderedQueryable<DaemonAudit> OrderEntries<T>(PagedDataOperators operators, int i,
            IOrderedQueryable<DaemonAudit> orderedQuery, IQueryable<DaemonAudit> query,
            Expression<Func<DaemonAudit, T>> expr)
        {
            if (i == 0)
                switch (operators.SortOrders[i].Direction)
                {
                    case "asc": orderedQuery = query.OrderBy(expr); break;
                    case "desc": orderedQuery = query.OrderByDescending(expr); break;
                }
            else
                switch (operators.SortOrders[i].Direction)
                {
                    case "asc": orderedQuery = orderedQuery?.ThenBy(expr); break;
                    case "desc": orderedQuery = orderedQuery?.ThenByDescending(expr); break;
                }
            return orderedQuery;
        }

        private static Expression<Func<DaemonAudit, R>> GetExpressionForOrdering<R>(MemberExpression prop, ParameterExpression param)
        {
            return Expression.Lambda<Func<DaemonAudit, R>>(prop, param);
        }

        private static IQueryable<T> WhereAll<T>(IQueryable<T> source, params Expression<Func<T, bool>>[] predicates)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicates == null) throw new ArgumentNullException(nameof(predicates));
            if (predicates.Length == 0) return source.Where(x => false);
            if (predicates.Length == 1) return source.Where(predicates[0]);

            Expression<Func<T, bool>> pred = null;
            for (var i = 0; i < predicates.Length; i++)
                pred = pred == null ? predicates[i] : pred.And(predicates[i]);
            return source.Where(pred);
        }
    }
}
