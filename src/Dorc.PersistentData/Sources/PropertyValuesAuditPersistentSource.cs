using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Extensions;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Dorc.PersistentData.Sources
{
    public class PropertyValuesAuditPersistentSource : IPropertyValuesAuditPersistentSource
    {
        private static readonly Dictionary<Type, Func<MemberExpression, ParameterExpression, IQueryable<Audit>, PagedDataOperators, int, IOrderedQueryable<Audit>, IOrderedQueryable<Audit>>> SortTypeHandlers = new()
        {
            [typeof(bool)] = (prop, param, q, ops, i, oq) => OrderScripts(ops, i, oq, q, GetExpressionForOrdering<bool>(prop, param)),
            [typeof(string)] = (prop, param, q, ops, i, oq) => OrderScripts(ops, i, oq, q, GetExpressionForOrdering<string>(prop, param)),
            [typeof(int)] = (prop, param, q, ops, i, oq) => OrderScripts(ops, i, oq, q, GetExpressionForOrdering<int>(prop, param)),
            [typeof(DateTime)] = (prop, param, q, ops, i, oq) => OrderScripts(ops, i, oq, q, GetExpressionForOrdering<DateTime>(prop, param)),
        };

        private readonly IDeploymentContextFactory _contextFactory;

        public PropertyValuesAuditPersistentSource(IDeploymentContextFactory contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public void AddRecord(long propertyId, long propertyValueId, string propertyName, string environmentName,
            string fromValue, string toValue, string updatedBy, string type)
        {
            if (string.Equals(type, "Update", StringComparison.OrdinalIgnoreCase) && toValue == fromValue) // Nothing to do
            {
                return;
            }

            using (var context = _contextFactory.GetContext())
            {
                //context.Configuration.ValidateOnSaveEnabled = true;
                var audit = new Audit
                {
                    PropertyId = propertyId,
                    PropertyValueId = propertyValueId,
                    PropertyName = propertyName,
                    EnvironmentName = environmentName,
                    FromValue = fromValue,
                    ToValue = toValue,
                    UpdatedBy = updatedBy,
                    UpdatedDate = DateTime.Now,
                    Type = type
                };
                context.Audits.Add(audit);
                context.SaveChanges();
            }
        }

        public GetPropertyValuesAuditListResponseDto GetPropertyValueAuditsByPage(int limit, int page, PagedDataOperators operators, bool useAndLogic)
        {
            using var context = _contextFactory.GetContext();

            var query = context.Audits.AsQueryable();
            query = ApplyFilters(query, operators, useAndLogic);
            var output = ApplySortingAndPaginate(query, operators, page, limit);

            return MapToResponse(output);
        }

        private static IQueryable<Audit> ApplyFilters(IQueryable<Audit> query, PagedDataOperators operators, bool useAndLogic)
        {
            if (operators.Filters == null || !operators.Filters.Any())
                return query;

            var filterLambdas = operators.Filters
                .Where(f => f != null && !string.IsNullOrEmpty(f.Path) && !string.IsNullOrEmpty(f.FilterValue))
                .Select(f => query.ContainsExpression(f.Path, f.FilterValue))
                .ToList();

            return useAndLogic
                ? WhereAll(query, filterLambdas.ToArray())
                : WhereAny(query, filterLambdas.ToArray());
        }

        private static PagedModel<Audit> ApplySortingAndPaginate(IQueryable<Audit> query, PagedDataOperators operators, int page, int limit)
        {
            if (operators.SortOrders == null || !operators.SortOrders.Any())
                return query.AsNoTracking().OrderBy(s => s.Id).Paginate(page, limit);

            var orderedQuery = BuildOrderedQuery(query, operators);

            return orderedQuery != null
                ? orderedQuery.AsNoTracking().Paginate(page, limit)
                : query.AsNoTracking().OrderBy(s => s.Id).Paginate(page, limit);
        }

        private static IOrderedQueryable<Audit> BuildOrderedQuery(IQueryable<Audit> query, PagedDataOperators operators)
        {
            IOrderedQueryable<Audit> orderedQuery = null;

            for (var i = 0; i < operators.SortOrders.Count; i++)
            {
                var sortOrder = operators.SortOrders[i];
                if (sortOrder == null || string.IsNullOrEmpty(sortOrder.Path) || string.IsNullOrEmpty(sortOrder.Direction))
                    continue;

                var param = Expression.Parameter(typeof(Audit), "Audit");
                var prop = Expression.PropertyOrField(param, sortOrder.Path);

                if (SortTypeHandlers.TryGetValue(prop.Type, out var handler))
                    orderedQuery = handler(prop, param, query, operators, i, orderedQuery);
            }

            return orderedQuery;
        }

        private static GetPropertyValuesAuditListResponseDto MapToResponse(PagedModel<Audit> output)
        {
            return new GetPropertyValuesAuditListResponseDto
            {
                CurrentPage = output.CurrentPage,
                TotalPages = output.TotalPages,
                TotalItems = output.TotalItems,
                Items = output.Items.Select(p => new PropertyValueAuditApiModel
                {
                    Id = p.Id,
                    EnvironmentName = p.EnvironmentName,
                    FromValue = p.FromValue,
                    PropertyId = p.PropertyId,
                    PropertyName = p.PropertyName,
                    PropertyValueId = p.PropertyValueId,
                    ToValue = p.ToValue,
                    UpdatedBy = p.UpdatedBy,
                    UpdatedDate = p.UpdatedDate,
                    Type = p.Type
                }).ToList()
            };
        }

        private static IOrderedQueryable<Audit> OrderScripts<T>(PagedDataOperators operators, int i, IOrderedQueryable<Audit> orderedQuery,
            IQueryable<Audit> scriptsQuery, Expression<Func<Audit, T>> expr)
        {
            if (i == 0)
                switch (operators.SortOrders[i].Direction)
                {
                    case "asc":
                        orderedQuery = scriptsQuery.OrderBy(expr);
                        break;

                    case "desc":
                        orderedQuery = scriptsQuery.OrderByDescending(expr);
                        break;
                }
            else
                switch (operators.SortOrders[i].Direction)
                {
                    case "asc":
                        orderedQuery = orderedQuery?.ThenBy(expr);
                        break;

                    case "desc":
                        orderedQuery = orderedQuery?.ThenByDescending(expr);
                        break;
                }

            return orderedQuery;
        }

        private static Expression<Func<Audit, R>> GetExpressionForOrdering<R>(MemberExpression prop, ParameterExpression param)
        {
            return Expression.Lambda<Func<Audit, R>>(prop, param);
        }

        private static IQueryable<T> WhereAll<T>(
            IQueryable<T> source,
            params Expression<Func<T, bool>>[] predicates)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicates == null) throw new ArgumentNullException(nameof(predicates));
            if (predicates.Length == 0) return source.Where(x => false); // no matches!
            if (predicates.Length == 1) return source.Where(predicates[0]); // simple

            Expression<Func<T, bool>> pred = null;
            for (var i = 0; i < predicates.Length; i++)
            {
                pred = pred == null
                    ? predicates[i]
                    : pred.And(predicates[i]);
            }
            return source.Where(pred);
        }
        private static IQueryable<T> WhereAny<T>(
            IQueryable<T> source,
            params Expression<Func<T, bool>>[] predicates)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicates == null) throw new ArgumentNullException(nameof(predicates));
            if (predicates.Length == 0) return source.Where(x => false);
            if (predicates.Length == 1) return source.Where(predicates[0]);

            Expression<Func<T, bool>> pred = null;
            for (var i = 0; i < predicates.Length; i++)
            {
                pred = pred == null
                    ? predicates[i]
                    : pred.Or(predicates[i]);
            }
            return source.Where(pred);
        }
    }
}