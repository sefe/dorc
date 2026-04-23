using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Security.Principal;
using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Extensions;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;

namespace Dorc.PersistentData.Sources
{
    public class RequestsStatusPersistentSource : IRequestsStatusPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;
        private readonly ILogger _log;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        public RequestsStatusPersistentSource(
            IDeploymentContextFactory contextFactory,
            ILogger<RequestsStatusPersistentSource> log,
            IClaimsPrincipalReader claimsPrincipalReader
            )
        {
            _contextFactory = contextFactory;
            _log = log;
            _claimsPrincipalReader = claimsPrincipalReader;
        }

        public GetRequestStatusesListResponseDto GetRequestStatusesByPage(int limit, int page, PagedDataOperators operators, IPrincipal principal)
        {
            string username = _claimsPrincipalReader.GetUserLogin(principal);
            var userSids = _claimsPrincipalReader.GetSidsForUser(principal);

            using var context = _contextFactory.GetContext();
            var reqStatusesQueryable = GetDeploymentRequestApiModels(context, username, userSids);

            reqStatusesQueryable = ApplyFilters(reqStatusesQueryable, operators);
            var output = ApplySortingAndPaginate(reqStatusesQueryable, operators, page, limit);

            return new GetRequestStatusesListResponseDto
            {
                CurrentPage = output.CurrentPage,
                TotalPages = output.TotalPages,
                TotalItems = output.TotalItems,
                Items = output.Items.ToList()
            };
        }

        private static IQueryable<DeploymentRequestApiModel> ApplyFilters(
            IQueryable<DeploymentRequestApiModel> query, PagedDataOperators operators)
        {
            if (operators.Filters == null || !operators.Filters.Any())
                return query;

            var detailFilters = operators.Filters
                .Where(f => f != null && (f.Path == "Project" || f.Path == "EnvironmentName" || f.Path == "BuildNumber"))
                .ToList();

            var hasDistinctDetailValues = detailFilters
                .Select(f => f.FilterValue)
                .Where(v => !string.IsNullOrEmpty(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() > 1;

            var filterLambdas = new List<Expression<Func<DeploymentRequestApiModel, bool>>>();

            foreach (var filter in operators.Filters)
            {
                if (filter == null)
                    continue;

                query = ApplySingleFilter(query, filter, hasDistinctDetailValues, filterLambdas);
            }

            if (filterLambdas.Count > 0)
                query = WhereAny(query, filterLambdas.ToArray());

            return query;
        }

        private static IQueryable<DeploymentRequestApiModel> ApplySingleFilter(
            IQueryable<DeploymentRequestApiModel> query,
            PagedDataFilter filter,
            bool hasDistinctDetailValues,
            List<Expression<Func<DeploymentRequestApiModel, bool>>> filterLambdas)
        {
            if (filter.Path == "EnvironmentNameExact")
            {
                var expr = query.ContainsExpression("EnvironmentName", filter.FilterValue);
                return expr != null ? query.Where(expr) : query;
            }

            if (filter.Path is "Project" or "EnvironmentName" or "BuildNumber")
            {
                if (string.IsNullOrEmpty(filter.FilterValue))
                    return query;

                var expr = query.ContainsExpression(filter.Path, filter.FilterValue);
                if (expr != null)
                {
                    if (hasDistinctDetailValues)
                        return query.Where(expr);

                    filterLambdas.Add(expr);
                }
                return query;
            }

            if (!string.IsNullOrEmpty(filter.Path) && !string.IsNullOrEmpty(filter.FilterValue))
            {
                var expr = query.ContainsExpression(filter.Path, filter.FilterValue);
                if (expr != null)
                    return query.Where(expr);
            }

            return query;
        }

        private static PagedModel<DeploymentRequestApiModel> ApplySortingAndPaginate(
            IQueryable<DeploymentRequestApiModel> query, PagedDataOperators operators, int page, int limit)
        {
            var orderedQuery = ApplySorting(query, operators);

            if (orderedQuery != null)
                return orderedQuery.AsNoTracking().Paginate(page, limit);

            return query.AsNoTracking().OrderBy(s => s.Id).Paginate(page, limit);
        }

        private static IOrderedQueryable<DeploymentRequestApiModel>? ApplySorting(
            IQueryable<DeploymentRequestApiModel> query, PagedDataOperators operators)
        {
            if (operators.SortOrders == null || !operators.SortOrders.Any())
                return null;

            IOrderedQueryable<DeploymentRequestApiModel>? orderedQuery = null;

            for (var i = 0; i < operators.SortOrders.Count; i++)
            {
                var sortOrder = operators.SortOrders[i];
                if (sortOrder == null || string.IsNullOrEmpty(sortOrder.Path) || string.IsNullOrEmpty(sortOrder.Direction))
                    continue;

                var param = Expression.Parameter(typeof(DeploymentRequestApiModel), "DeploymentRequest");
                var prop = Expression.PropertyOrField(param, sortOrder.Path);

                orderedQuery = ApplySortByType(prop, param, operators, i, orderedQuery, query);
            }

            return orderedQuery;
        }

        private static IOrderedQueryable<DeploymentRequestApiModel>? ApplySortByType(
            MemberExpression prop, ParameterExpression param, PagedDataOperators operators, int i,
            IOrderedQueryable<DeploymentRequestApiModel>? orderedQuery, IQueryable<DeploymentRequestApiModel> query)
        {
            return prop.Type switch
            {
                Type t when t == typeof(bool) =>
                    OrderScripts(operators, i, orderedQuery, query, GetExpressionForOrdering<bool>(prop, param)),
                Type t when t == typeof(string) =>
                    OrderScripts(operators, i, orderedQuery, query, GetExpressionForOrdering<string>(prop, param)),
                Type t when t == typeof(int) =>
                    OrderScripts(operators, i, orderedQuery, query, GetExpressionForOrdering<int>(prop, param)),
                _ => orderedQuery
            };
        }

        public void AppendLogToJob(int deploymentResultId, string log)
        {
            _log.LogDebug($"Initialising Context");
            using var context = _contextFactory.GetContext();
            _log.LogDebug($"Initialising Context...Done");
            _log.LogDebug($"Execute SQL");
            context.DeploymentResults
            .Where(dr => dr.Id==deploymentResultId)
            .ExecuteUpdate(setters =>
                setters.SetProperty(u => u.Log, u => u.Log + System.Environment.NewLine + log));
            _log.LogDebug($"Execute SQL...Done");
        }

        public static IQueryable<DeploymentRequestApiModel> GetDeploymentRequestApiModels(IDeploymentContext context, string userName, List<string> userSids)
        {
            var reqStatusesQueryable = from req in context.DeploymentRequests
                                       join environment in context.Environments on req.Environment equals
                                           environment.Name
                                       let permissions =
                                           (from env in context.Environments
                                            join ac in context.AccessControls on env.ObjectId equals ac.ObjectId
                                            where env.Name == environment.Name && (EF.Constant(userSids).Contains(ac.Sid) || ac.Pid != null && EF.Constant(userSids).Contains(ac.Pid))
                                            select ac.Allow).ToList()
                                       let isPermissioned =
                                           permissions.Any(p => (p & (int)(AccessLevel.Write | AccessLevel.Owner)) != 0)
                                       let isOwner =
                                           permissions.Any(p => (p & (int)(AccessLevel.Owner)) != 0)
                                       select new DeploymentRequestApiModel
                                       {
                                           Id = req.Id,
                                           Components = req.Components,
                                           RequestedTime = req.RequestedTime,
                                           StartedTime = req.StartedTime,
                                           CompletedTime = req.CompletedTime,
                                           Status = req.Status,
                                           Log = req.Log,
                                           IsProd = req.IsProd,
                                           Project = req.Project,
                                           EnvironmentName = req.Environment,
                                           BuildNumber = req.BuildNumber,
                                           BuildUri = req.BuildUri,
                                           UserName = req.UserName,
                                           RequestDetails = req.RequestDetails,
                                           UncLogPath = req.UncLogPath,
                                           CancelledBy = req.CancelledBy,
                                           CancelledTime = req.CancelledTime,
                                           UserEditable = isOwner  || isPermissioned
                                       };

            //var sql = reqStatusesQueryable.ToString();
            //var items = reqStatusesQueryable.ToList();
            return reqStatusesQueryable;
        }

        private static IOrderedQueryable<DeploymentRequestApiModel> OrderScripts<T>(PagedDataOperators operators, int i, IOrderedQueryable<DeploymentRequestApiModel> orderedQuery,
            IQueryable<DeploymentRequestApiModel> scriptsQuery, Expression<Func<DeploymentRequestApiModel, T>> expr)
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

        private static Expression<Func<DeploymentRequestApiModel, R>> GetExpressionForOrdering<R>(MemberExpression prop, ParameterExpression param)
        {
            return Expression.Lambda<Func<DeploymentRequestApiModel, R>>(prop, param);
        }

        private static IQueryable<T> WhereAny<T>(
            IQueryable<T> source,
            params Expression<Func<T, bool>>[] predicates)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicates == null) throw new ArgumentNullException(nameof(predicates));
            if (predicates.Length == 0) return source.Where(x => false); // no matches!

            var predicate = predicates[0];
            for (var i = 1; i < predicates.Length; i++)
            {
                predicate = predicate.Or(predicates[i]);
            }
            return source.Where(predicate);
        }

        public void SetUncLogPathforRequest(int requestId, string uncLogPath)
        {
            using (var context = _contextFactory.GetContext())
            {
                context.DeploymentRequests.FirstOrDefault(dr => dr.Id == requestId).UncLogPath = uncLogPath;

                context.SaveChanges();
            }
        }
    }
}
