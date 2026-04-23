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
            using var context = _contextFactory.GetContext();
            var queryable = context.AuditScripts.AsQueryable();

            queryable = ApplyFilters(queryable, operators, useAndLogic);
            var output = ApplySortingAndPaginate(queryable, operators, page, limit)
                         ?? queryable.AsNoTracking().OrderBy(s => s.Id).Paginate(page, limit);

            return MapToDto(output);
        }

        private static IQueryable<AuditScript> ApplyFilters(IQueryable<AuditScript> queryable,
            PagedDataOperators operators, bool useAndLogic)
        {
            if (operators.Filters == null || !operators.Filters.Any())
                return queryable;

            var filterLambdas = operators.Filters
                .Where(f => f != null && !string.IsNullOrEmpty(f.Path) && !string.IsNullOrEmpty(f.FilterValue))
                .Select(f => queryable.ContainsExpression(f.Path, f.FilterValue))
                .ToList();

            if (!filterLambdas.Any())
                return queryable;

            return useAndLogic
                ? WhereAll(queryable, filterLambdas.ToArray())
                : WhereAny(queryable, filterLambdas.ToArray());
        }

        private static PagedModel<AuditScript>? ApplySortingAndPaginate(IQueryable<AuditScript> queryable,
            PagedDataOperators operators, int page, int limit)
        {
            if (operators.SortOrders == null || !operators.SortOrders.Any())
                return null;

            IOrderedQueryable<AuditScript>? orderedQuery = null;

            for (var i = 0; i < operators.SortOrders.Count; i++)
            {
                var sortOrder = operators.SortOrders[i];
                if (sortOrder == null || string.IsNullOrEmpty(sortOrder.Path) || string.IsNullOrEmpty(sortOrder.Direction))
                    continue;

                orderedQuery = ApplySortOrder(queryable, orderedQuery, sortOrder, i == 0);
            }

            return orderedQuery?.AsNoTracking().Paginate(page, limit);
        }

        private static IOrderedQueryable<AuditScript>? ApplySortOrder(IQueryable<AuditScript> queryable,
            IOrderedQueryable<AuditScript>? orderedQuery, PagedDataSorting sortOrder, bool isFirst)
        {
            var param = Expression.Parameter(typeof(AuditScript), "AuditScript");
            var prop = Expression.PropertyOrField(param, sortOrder.Path);

            return prop.Type switch
            {
                Type t when t == typeof(bool) => OrderEntries(sortOrder.Direction, isFirst, orderedQuery, queryable, GetExpressionForOrdering<bool>(prop, param)),
                Type t when t == typeof(string) => OrderEntries(sortOrder.Direction, isFirst, orderedQuery, queryable, GetExpressionForOrdering<string>(prop, param)),
                Type t when t == typeof(int) => OrderEntries(sortOrder.Direction, isFirst, orderedQuery, queryable, GetExpressionForOrdering<int>(prop, param)),
                Type t when t == typeof(DateTime) => OrderEntries(sortOrder.Direction, isFirst, orderedQuery, queryable, GetExpressionForOrdering<DateTime>(prop, param)),
                _ => orderedQuery
            };
        }

        private static GetScriptsAuditListResponseDto MapToDto(PagedModel<AuditScript> output)
        {
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

        private static IOrderedQueryable<AuditScript> OrderEntries<T>(string direction, bool isFirst,
            IOrderedQueryable<AuditScript>? orderedQuery, IQueryable<AuditScript> query,
            Expression<Func<AuditScript, T>> expr)
        {
            if (isFirst)
                return direction == "desc" ? query.OrderByDescending(expr) : query.OrderBy(expr);

            return direction == "desc" ? orderedQuery!.ThenByDescending(expr) : orderedQuery!.ThenBy(expr);
        }

        private static Expression<Func<AuditScript, R>> GetExpressionForOrdering<R>(MemberExpression prop, ParameterExpression param)
        {
            return Expression.Lambda<Func<AuditScript, R>>(prop, param);
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

        private static IQueryable<T> WhereAny<T>(IQueryable<T> source, params Expression<Func<T, bool>>[] predicates)
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