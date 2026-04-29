using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Extensions;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Dorc.PersistentData.Sources
{
    public class ServersAuditPersistentSource : IServersAuditPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;
        public ServersAuditPersistentSource(IDeploymentContextFactory contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public void InsertServerAudit(string username, ActionType action, int? serverId, string? fromValue, string? toValue)
        {
            // Skip no-op Updates (matching ScriptsAuditPersistentSource.AddRecord convention)
            if (action == ActionType.Update && string.Equals(fromValue, toValue))
            {
                return;
            }

            using (var context = _contextFactory.GetContext())
            {
                var actionRow = context.RefDataAuditActions.First(x => x.Action == action);

                var audit = new ServerAudit
                {
                    Date = DateTime.Now,
                    Username = username,
                    ServerId = serverId,
                    RefDataAuditActionId = actionRow.RefDataAuditActionId,
                    Action = actionRow,
                    FromValue = fromValue,
                    ToValue = toValue
                };

                context.ServerAudits.Add(audit);
                context.SaveChanges();
            }
        }

        public GetServerAuditListResponseDto GetServerAudit(int limit, int page, PagedDataOperators operators)
        {
            using (var context = _contextFactory.GetContext())
            {
                var queryable = context.ServerAudits
                    .Include(a => a.Action)
                    .AsQueryable();

                return RunPagedQuery(context, queryable, limit, page, operators);
            }
        }

        private static GetServerAuditListResponseDto RunPagedQuery(
    IDeploymentContext context,
    IQueryable<ServerAudit> queryable,
    int limit, int page, PagedDataOperators operators)
        {
            PagedModel<ServerAudit> output = null;

            var filterLambdas = new List<Expression<Func<ServerAudit, bool>>>();

            if (operators.Filters != null && operators.Filters.Any())
            {
                var validFilters = operators.Filters
                    .Where(f => f != null
                        && !string.IsNullOrEmpty(f.Path)
                        && !string.IsNullOrEmpty(f.FilterValue));

                foreach (var pagedDataFilter in validFilters)
                {
                    var expr = queryable.ContainsExpression(pagedDataFilter.Path, pagedDataFilter.FilterValue);
                    if (expr != null)
                    {
                        filterLambdas.Add(expr);
                    }
                }
            }

            if (filterLambdas.Count > 0)
            {
                queryable = WhereAll(queryable, filterLambdas.ToArray());
            }

            if (operators.SortOrders != null && operators.SortOrders.Any())
            {
                IOrderedQueryable<ServerAudit> orderedQuery = null;

                for (var i = 0; i < operators.SortOrders.Count; i++)
                {
                    if (operators.SortOrders[i] == null) continue;
                    if (string.IsNullOrEmpty(operators.SortOrders[i].Path) ||
                        string.IsNullOrEmpty(operators.SortOrders[i].Direction))
                        continue;

                    var param = Expression.Parameter(typeof(ServerAudit), "ServerAudit");
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

            var pagedServerIds = output.Items
                .Where(a => a.ServerId.HasValue)
                .Select(a => a.ServerId!.Value)
                .Distinct()
                .ToList();

            var serverNamesById = pagedServerIds.Count == 0
                ? new Dictionary<int, string>()
                : context.Servers
                    .Where(d => pagedServerIds.Contains(d.Id))
                    .AsNoTracking()
                    .ToDictionary(d => d.Id, d => d.Name);

            return new GetServerAuditListResponseDto
            {
                CurrentPage = output.CurrentPage,
                TotalPages = output.TotalPages,
                TotalItems = output.TotalItems,
                Items = output.Items.Select(a => new ServerAuditApiModel
                {
                    Id = a.Id,
                    ServerId = a.ServerId,
                    ServerName = a.ServerId.HasValue && serverNamesById.TryGetValue(a.ServerId.Value, out var name)
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

        private static IOrderedQueryable<ServerAudit> OrderEntries<T>(PagedDataOperators operators, int i,
            IOrderedQueryable<ServerAudit> orderedQuery, IQueryable<ServerAudit> query,
            Expression<Func<ServerAudit, T>> expr)
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

        private static Expression<Func<ServerAudit, R>> GetExpressionForOrdering<R>(MemberExpression prop, ParameterExpression param)
        {
            return Expression.Lambda<Func<ServerAudit, R>>(prop, param);
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
