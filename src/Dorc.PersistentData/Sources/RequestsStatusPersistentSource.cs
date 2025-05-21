using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Security.Principal;
using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Extensions;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;

namespace Dorc.PersistentData.Sources
{
    public class RequestsStatusPersistentSource : IRequestsStatusPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;
        private readonly ILog _log;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        public RequestsStatusPersistentSource(
            IDeploymentContextFactory contextFactory,
            ILog log,
            IClaimsPrincipalReader claimsPrincipalReader
            )
        {
            _contextFactory = contextFactory;
            _log = log;
            _claimsPrincipalReader = claimsPrincipalReader;
        }

        public GetRequestStatusesListResponseDto GetRequestStatusesByPage(int limit, int page, PagedDataOperators operators, IPrincipal user)
        {
            string username = _claimsPrincipalReader.GetUserName(user);
            var userSids = _claimsPrincipalReader.GetSidsForUser(user);

            PagedModel<DeploymentRequestApiModel> output = null;
            using (var context = _contextFactory.GetContext())
            {
                var reqStatusesQueryable = GetDeploymentRequestApiModels(context, username, userSids);

                if (operators.Filters != null && operators.Filters.Any())
                {
                    var filterLambdas =
                        new List<Expression<Func<DeploymentRequestApiModel, bool>>>();
                    foreach (var pagedDataFilter in operators.Filters)
                    {
                        if (pagedDataFilter == null)
                            continue;
                        if (pagedDataFilter.Path == "Project" || pagedDataFilter.Path == "EnvironmentName" ||
                            pagedDataFilter.Path == "BuildNumber") // this isn't pleasant but given this is built specifically for the UI
                        {
                            var containsExpression = reqStatusesQueryable.ContainsExpression(pagedDataFilter.Path,
                                pagedDataFilter.FilterValue);
                            if (containsExpression != null)
                                filterLambdas.Add(containsExpression);
                            continue;
                        }
                        if (!string.IsNullOrEmpty(pagedDataFilter.Path) && !string.IsNullOrEmpty(pagedDataFilter.FilterValue))
                        {
                            var containsExpression = reqStatusesQueryable.ContainsExpression(pagedDataFilter.Path,
                                pagedDataFilter.FilterValue);

                            if (containsExpression != null)
                                reqStatusesQueryable =
                                    reqStatusesQueryable.Where(containsExpression);
                        }
                    }

                    if (filterLambdas.Count > 0)
                        reqStatusesQueryable = WhereAny(reqStatusesQueryable, filterLambdas.ToArray());
                }

                if (operators.SortOrders != null && operators.SortOrders.Any())
                {
                    IOrderedQueryable<DeploymentRequestApiModel> orderedQuery = null;

                    for (var i = 0; i < operators.SortOrders.Count; i++)
                    {
                        if (operators.SortOrders[i] == null)
                            continue;
                        if (string.IsNullOrEmpty(operators.SortOrders[i].Path) ||
                            string.IsNullOrEmpty(operators.SortOrders[i].Direction))
                            continue;

                        var param = Expression.Parameter(typeof(DeploymentRequestApiModel), "DeploymentRequest");
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

                return new GetRequestStatusesListResponseDto
                {
                    CurrentPage = output.CurrentPage,
                    TotalPages = output.TotalPages,
                    TotalItems = output.TotalItems,
                    Items = output.Items.ToList()
                };
            }
        }

        public void AppendLogToJob(int deploymentResultId, string log)
        {
            _log.Debug($"Initialising Context");
            using var context = _contextFactory.GetContext();
            _log.Debug($"Initialising Context...Done");
            _log.Debug($"Execute SQL");
            context.DeploymentResults
            .Where(dr => dr.Id==deploymentResultId)
            .ExecuteUpdate(setters =>
                setters.SetProperty(u => u.Log, u => u.Log + System.Environment.NewLine + log));
            _log.Debug($"Execute SQL...Done");
        }

        public static IQueryable<DeploymentRequestApiModel> GetDeploymentRequestApiModels(IDeploymentContext context, string userName, List<string> userSids)
        {
            var reqStatusesQueryable = from req in context.DeploymentRequests
                                       join environment in context.Environments on req.Environment equals
                                           environment.Name
                                       let isOwner = environment.Owner == userName
                                       let isDelegate =
                                           (from envDetail in context.Environments
                                            join env in context.Environments on envDetail.Name equals env.Name
                                            where env.Name == environment.Name && envDetail.Users.Select(u => u.LoginId).Contains(userName)
                                            select envDetail.Name).Any()
                                       let isPermissioned =
                                           (from envDetail in context.Environments
                                            join env in context.Environments on envDetail.Name equals env.Name
                                            join ac in context.AccessControls on env.ObjectId equals ac.ObjectId
                                            where env.Name == environment.Name && (userSids.Contains(ac.Sid) || ac.Pid != null && userSids.Contains(ac.Pid)) && (ac.Allow & (int)(AccessLevel.Write | AccessLevel.Owner)) != 0
                                            select envDetail.Name).Any()
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
                                           UserName = req.UserName,
                                           RequestDetails = req.RequestDetails,
                                           UncLogPath = req.UncLogPath,
                                           UserEditable = isOwner || isDelegate || isPermissioned
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
