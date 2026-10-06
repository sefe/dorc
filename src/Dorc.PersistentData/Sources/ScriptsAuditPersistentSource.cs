using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Extensions;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Dorc.PersistentData.Sources
{
    public class ScriptsAuditPersistentSource : IScriptsAuditPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;

        public ScriptsAuditPersistentSource(IDeploymentContextFactory contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public void AddRecord(long scriptId, string scriptName, string fromValue, string toValue,
            string updatedBy, string type, string projectNames)
        {
            if (string.Equals(type, "Update", StringComparison.OrdinalIgnoreCase) && toValue == fromValue)
            {
                return;
            }

            using (var context = _contextFactory.GetContext())
            {
                var audit = new AuditScript
                {
                    ScriptId = scriptId,
                    ScriptName = scriptName,
                    FromValue = fromValue,
                    ToValue = toValue,
                    UpdatedBy = updatedBy,
                    UpdatedDate = DateTime.Now,
                    Type = type,
                    ProjectNames = projectNames
                };
                context.AuditScripts.Add(audit);
                context.SaveChanges();
            }
        }

        public GetScriptsAuditListResponseDto GetScriptAuditsByPage(int limit, int page,
            PagedDataOperators operators, bool useAndLogic)
        {
            PagedModel<AuditScript> output = null;
            using (var context = _contextFactory.GetContext())
            {
                var queryable = context.AuditScripts.AsQueryable();

                if (operators.Filters != null && operators.Filters.Any())
                {
                    var filterLambdas = new List<Expression<Func<AuditScript, bool>>>();
                    foreach (var pagedDataFilter in operators.Filters)
                    {
                        if (pagedDataFilter == null)
                            continue;
                        if (!string.IsNullOrEmpty(pagedDataFilter.Path) && !string.IsNullOrEmpty(pagedDataFilter.FilterValue))
                        {
                            filterLambdas.Add(queryable.ContainsExpression(pagedDataFilter.Path,
                                pagedDataFilter.FilterValue));
                        }
                    }

                    if (useAndLogic)
                        queryable = WhereAll(queryable, filterLambdas.ToArray());
                    else
                        queryable = WhereAny(queryable, filterLambdas.ToArray());
                }

                if (operators.SortOrders != null && operators.SortOrders.Any())
                {
                    IOrderedQueryable<AuditScript> orderedQuery = null;

                    for (var i = 0; i < operators.SortOrders.Count; i++)
                    {
                        if (operators.SortOrders[i] == null)
                            continue;
                        if (string.IsNullOrEmpty(operators.SortOrders[i].Path) ||
                            string.IsNullOrEmpty(operators.SortOrders[i].Direction))
                            continue;

                        var param = Expression.Parameter(typeof(AuditScript), "AuditScript");
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
                    output = queryable.AsNoTracking().OrderBy(s => s.Id).Paginate(page, limit);

                return new GetScriptsAuditListResponseDto
                {
                    CurrentPage = output.CurrentPage,
                    TotalPages = output.TotalPages,
                    TotalItems = output.TotalItems,
                    Items = output.Items.Select(p => new ScriptAuditApiModel
                    {
                        Id = p.Id,
                        ScriptId = p.ScriptId,
                        ScriptName = p.ScriptName,
                        FromValue = p.FromValue,
                        ToValue = p.ToValue,
                        UpdatedBy = p.UpdatedBy,
                        UpdatedDate = p.UpdatedDate,
                        Type = p.Type,
                        ProjectNames = p.ProjectNames
                    }).ToList()
                };
            }
        }

        private static IOrderedQueryable<AuditScript> OrderEntries<T>(PagedDataOperators operators, int i,
            IOrderedQueryable<AuditScript> orderedQuery, IQueryable<AuditScript> query,
            Expression<Func<AuditScript, T>> expr)
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

        private static Expression<Func<AuditScript, R>> GetExpressionForOrdering<R>(MemberExpression prop, ParameterExpression param)
        {
            return Expression.Lambda<Func<AuditScript, R>>(prop, param);
        }

        private IQueryable<T> WhereAll<T>(IQueryable<T> source, params Expression<Func<T, bool>>[] predicates)
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

        private IQueryable<T> WhereAny<T>(IQueryable<T> source, params Expression<Func<T, bool>>[] predicates)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicates == null) throw new ArgumentNullException(nameof(predicates));
            if (predicates.Length == 0) return source.Where(x => false);
            if (predicates.Length == 1) return source.Where(predicates[0]);

            Expression<Func<T, bool>> pred = null;
            for (var i = 0; i < predicates.Length; i++)
                pred = pred == null ? predicates[i] : pred.Or(predicates[i]);
            return source.Where(pred);
        }
    }
}