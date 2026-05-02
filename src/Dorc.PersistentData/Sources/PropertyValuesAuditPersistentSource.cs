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
            PagedModel<Audit> output = null;
            using (var context = _contextFactory.GetContext())
            {
                var reqStatusesQueryable = context.Audits.AsQueryable();

                if (operators.Filters != null && operators.Filters.Any())
                {
                    var filterLambdas =
                        new List<Expression<Func<Audit, bool>>>();
                    foreach (var pagedDataFilter in operators.Filters)
                    {
                        if (pagedDataFilter == null)
                            continue;
                        if (!string.IsNullOrEmpty(pagedDataFilter.Path) && !string.IsNullOrEmpty(pagedDataFilter.FilterValue))
                        {
                            filterLambdas.Add(reqStatusesQueryable.ContainsExpression(pagedDataFilter.Path,
                                    pagedDataFilter.FilterValue));
                        }
                    }

                    if (useAndLogic)
                        reqStatusesQueryable = WhereAll(reqStatusesQueryable, filterLambdas.ToArray());
                    else
                        reqStatusesQueryable = WhereAny(reqStatusesQueryable, filterLambdas.ToArray());
                }

                if (operators.SortOrders != null && operators.SortOrders.Any())
                {
                    IOrderedQueryable<Audit> orderedQuery = null;

                    for (var i = 0; i < operators.SortOrders.Count; i++)
                    {
                        if (operators.SortOrders[i] == null)
                            continue;
                        if (string.IsNullOrEmpty(operators.SortOrders[i].Path) ||
                            string.IsNullOrEmpty(operators.SortOrders[i].Direction))
                            continue;

                        var param = Expression.Parameter(typeof(Audit), "Audit");
                        var prop = Expression.PropertyOrField(param, operators.SortOrders[i].Path);

                        switch (prop.Type)
                        {
                            case Type boolType when boolType == typeof(bool):
                                {
                                    var expr = GetExpressionForOrdering<bool>(prop, param);
                                    orderedQuery = OrderScripts(operators, i, orderedQuery, reqStatusesQueryable, expr);
                                    break;
                                }
                            case Type stringType when stringType == typeof(string):
                                {
                                    var expr = GetExpressionForOrdering<string>(prop, param);
                                    orderedQuery = OrderScripts(operators, i, orderedQuery, reqStatusesQueryable, expr);
                                    break;
                                }
                            case Type intType when intType == typeof(int):
                                {
                                    var expr = GetExpressionForOrdering<int>(prop, param);
                                    orderedQuery = OrderScripts(operators, i, orderedQuery, reqStatusesQueryable, expr);
                                    break;
                                }
                            case Type datetimeType when datetimeType == typeof(DateTime):
                                {
                                    var expr = GetExpressionForOrdering<DateTime>(prop, param);
                                    orderedQuery = OrderScripts(operators, i, orderedQuery, reqStatusesQueryable, expr);
                                    break;
                                }
                        }
                    }

                    if (orderedQuery != null)
                        output = orderedQuery.AsNoTracking()
                            .Paginate(page, limit);
                }

                if (output == null)
                    output = reqStatusesQueryable.AsNoTracking()
                        .OrderBy(s => s.Id)
                        .Paginate(page, limit);

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

        private IQueryable<T> WhereAll<T>(
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
        private IQueryable<T> WhereAny<T>(
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