using Dorc.ApiModel;
using Dorc.PersistentData.Extensions;
using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Dorc.PersistentData.Sources
{
    /// <summary>
    /// Shared filter/sort/page/project pipeline for the environment-component
    /// audit read endpoints (containers, cloud resources, API registrations),
    /// mirroring DaemonAuditPersistentSource.RunPagedQuery. The component-name
    /// join is resolved post-page via a dictionary lookup against the paged
    /// ids — there is deliberately no FK between the audit rows and the
    /// component tables (audit history survives component deletion) and no EF
    /// navigation property, so one round-trip after pagination is cheaper than
    /// a per-row join in SQL.
    /// </summary>
    internal static class ComponentAuditQuery
    {
        internal static GetComponentAuditListResponseDto RunPagedQuery<TAudit>(
            IQueryable<TAudit> queryable,
            int limit, int page, PagedDataOperators operators,
            Func<IReadOnlyCollection<int>, Dictionary<int, string>> componentNamesByIds)
            where TAudit : class, IComponentAudit
        {
            PagedModel<TAudit> output = null;

            var filterLambdas = new List<Expression<Func<TAudit, bool>>>();

            if (operators.Filters != null && operators.Filters.Any())
            {
                var validFilters = operators.Filters
                    .Where(f => f != null
                        && !string.IsNullOrEmpty(f.Path)
                        && !string.IsNullOrEmpty(f.FilterValue));

                foreach (var pagedDataFilter in validFilters)
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

            // WhereAll treats an empty predicate list as "match nothing" (x => false), so
            // only apply it when we actually have user-supplied filters.
            if (filterLambdas.Count > 0)
            {
                queryable = WhereAll(queryable, filterLambdas.ToArray());
            }

            if (operators.SortOrders != null && operators.SortOrders.Any())
            {
                IOrderedQueryable<TAudit> orderedQuery = null;

                for (var i = 0; i < operators.SortOrders.Count; i++)
                {
                    if (operators.SortOrders[i] == null) continue;
                    if (string.IsNullOrEmpty(operators.SortOrders[i].Path) ||
                        string.IsNullOrEmpty(operators.SortOrders[i].Direction))
                        continue;

                    var param = Expression.Parameter(typeof(TAudit), "ComponentAudit");
                    var prop = Expression.PropertyOrField(param, operators.SortOrders[i].Path);

                    switch (prop.Type)
                    {
                        case Type boolType when boolType == typeof(bool):
                            orderedQuery = OrderEntries(operators, i, orderedQuery, queryable,
                                GetExpressionForOrdering<TAudit, bool>(prop, param));
                            break;
                        case Type stringType when stringType == typeof(string):
                            orderedQuery = OrderEntries(operators, i, orderedQuery, queryable,
                                GetExpressionForOrdering<TAudit, string>(prop, param));
                            break;
                        case Type intType when intType == typeof(int):
                            orderedQuery = OrderEntries(operators, i, orderedQuery, queryable,
                                GetExpressionForOrdering<TAudit, int>(prop, param));
                            break;
                        case Type datetimeType when datetimeType == typeof(DateTime):
                            orderedQuery = OrderEntries(operators, i, orderedQuery, queryable,
                                GetExpressionForOrdering<TAudit, DateTime>(prop, param));
                            break;
                    }
                }

                if (orderedQuery != null)
                    output = orderedQuery.AsNoTracking().Paginate(page, limit);
            }

            if (output == null)
                output = queryable.AsNoTracking().OrderByDescending(a => a.Date).Paginate(page, limit);

            var pagedComponentIds = output.Items
                .Where(a => a.ComponentId.HasValue)
                .Select(a => a.ComponentId!.Value)
                .Distinct()
                .ToList();

            var namesById = pagedComponentIds.Count == 0
                ? new Dictionary<int, string>()
                : componentNamesByIds(pagedComponentIds);

            return new GetComponentAuditListResponseDto
            {
                CurrentPage = output.CurrentPage,
                TotalPages = output.TotalPages,
                TotalItems = output.TotalItems,
                Items = output.Items.Select(a => new ComponentAuditApiModel
                {
                    Id = a.Id,
                    ComponentId = a.ComponentId,
                    ComponentName = a.ComponentId.HasValue && namesById.TryGetValue(a.ComponentId.Value, out var name)
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

        private static IOrderedQueryable<TAudit> OrderEntries<TAudit, T>(PagedDataOperators operators, int i,
            IOrderedQueryable<TAudit> orderedQuery, IQueryable<TAudit> query,
            Expression<Func<TAudit, T>> expr)
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

        private static Expression<Func<TAudit, R>> GetExpressionForOrdering<TAudit, R>(
            MemberExpression prop, ParameterExpression param)
        {
            return Expression.Lambda<Func<TAudit, R>>(prop, param);
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
