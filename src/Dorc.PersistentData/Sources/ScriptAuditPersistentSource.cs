using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Text.Json;
using log4net;
using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Extensions;
using Dorc.PersistentData.Contexts;

namespace Dorc.PersistentData.Sources
{
    public class ScriptAuditPersistentSource : IScriptAuditPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;
        private readonly ILog _logger;

        public ScriptAuditPersistentSource(
            IDeploymentContextFactory contextFactory,
            ILog logger
            )
        {
            _logger = logger;
            _contextFactory = contextFactory;
        }

        public void InsertScriptAudit(string username, ActionType actionType, ScriptApiModel scriptApiModel)
        {
            using (var context = _contextFactory.GetContext())
            {
                var scriptAudit = new ScriptAudit
                {
                    Date = DateTime.Now,
                    Username = username,
                    ScriptId = scriptApiModel.Id,
                    Json = JsonSerializer.Serialize(scriptApiModel, new JsonSerializerOptions { WriteIndented = true }),
                    Action = context.ScriptAuditActions.First(x => x.Action == actionType)
                };

                context.ScriptAudits.Add(scriptAudit);
                context.SaveChanges();
            }
        }

        public GetScriptAuditListResponseDto GetScriptAuditByScriptId(int scriptId, int limit, int page, PagedDataOperators operators)
        {
            PagedModel<ScriptAudit> output = null;
            using (var context = _contextFactory.GetContext())
            {
                var scriptAuditsQueryable = context.ScriptAudits
                    .Include(scriptAudit => scriptAudit.Action)
                    .Include(scriptAudit => scriptAudit.Script)
                    .AsQueryable();

                var filterLambdas =
                    new List<Expression<Func<ScriptAudit, bool>>>();
                filterLambdas.Add(scriptAuditsQueryable.ContainsExpression(nameof(ScriptAudit.ScriptId),
                                scriptId.ToString()));
                if (operators.Filters != null && operators.Filters.Any())
                {
                    foreach (var pagedDataFilter in operators.Filters)
                    {
                        if (pagedDataFilter == null)
                            continue;
                        if (!string.IsNullOrEmpty(pagedDataFilter.Path) && !string.IsNullOrEmpty(pagedDataFilter.FilterValue))
                        {
                            filterLambdas.Add(scriptAuditsQueryable.ContainsExpression(pagedDataFilter.Path,
                                    pagedDataFilter.FilterValue));
                        }
                    }
                }
                scriptAuditsQueryable = WhereAll(scriptAuditsQueryable, filterLambdas.ToArray());

                if (operators.SortOrders != null && operators.SortOrders.Any())
                {
                    IOrderedQueryable<ScriptAudit> orderedQuery = null;

                    for (var i = 0; i < operators.SortOrders.Count; i++)
                    {
                        if (operators.SortOrders[i] == null)
                            continue;
                        if (string.IsNullOrEmpty(operators.SortOrders[i].Path) ||
                            string.IsNullOrEmpty(operators.SortOrders[i].Direction))
                            continue;

                        var param = Expression.Parameter(typeof(ScriptAudit), "ScriptAudit");
                        var prop = Expression.PropertyOrField(param, operators.SortOrders[i].Path);

                        switch (prop.Type)
                        {
                            case Type boolType when boolType == typeof(bool):
                                {
                                    var expr = GetExpressionForOrdering<bool>(prop, param);
                                    orderedQuery = OrderScripts(operators, i, orderedQuery, scriptAuditsQueryable, expr);
                                    break;
                                }
                            case Type stringType when stringType == typeof(string):
                                {
                                    var expr = GetExpressionForOrdering<string>(prop, param);
                                    orderedQuery = OrderScripts(operators, i, orderedQuery, scriptAuditsQueryable, expr);
                                    break;
                                }
                            case Type intType when intType == typeof(int):
                                {
                                    var expr = GetExpressionForOrdering<int>(prop, param);
                                    orderedQuery = OrderScripts(operators, i, orderedQuery, scriptAuditsQueryable, expr);
                                    break;
                                }
                            case Type datetimeType when datetimeType == typeof(DateTime):
                                {
                                    var expr = GetExpressionForOrdering<DateTime>(prop, param);
                                    orderedQuery = OrderScripts(operators, i, orderedQuery, scriptAuditsQueryable, expr);
                                    break;
                                }
                        }
                    }

                    if (orderedQuery != null)
                        output = orderedQuery.AsNoTracking()
                            .Paginate(page, limit);
                }

                if (output == null)
                    output = scriptAuditsQueryable.AsNoTracking()
                        .OrderByDescending(s => s.Date)
                        .Paginate(page, limit);

                return new GetScriptAuditListResponseDto
                {
                    CurrentPage = output.CurrentPage,
                    TotalPages = output.TotalPages,
                    TotalItems = output.TotalItems,
                    Items = output.Items.Select(scriptAudit => new ScriptAuditApiModel
                    {
                        ScriptAuditId = scriptAudit.ScriptAuditId,
                        ScriptId = scriptAudit.ScriptId,
                        Script = new ScriptApiModel
                        {
                            Id = scriptAudit.Script.Id,
                            Name = scriptAudit.Script.Name,
                            Path = scriptAudit.Script.Path,
                            IsPathJSON = scriptAudit.Script.IsPathJSON,
                            NonProdOnly = scriptAudit.Script.NonProdOnly,
                            PowerShellVersionNumber = scriptAudit.Script.PowerShellVersionNumber
                        },
                        ScriptAuditActionId = scriptAudit.ScriptAuditActionId,
                        Action = scriptAudit.Action.Action.ToString(),
                        Username = scriptAudit.Username,
                        Date = scriptAudit.Date,
                        Json = scriptAudit.Json
                    }).ToList()
                };
            }
        }

        private static IQueryable<TEntity> WhereAll<TEntity>(IQueryable<TEntity> query, params Expression<Func<TEntity, bool>>[] predicates)
        {
            return predicates.Aggregate(query, (current, predicate) => current.Where(predicate));
        }

        private static IOrderedQueryable<ScriptAudit> OrderScripts<T>(PagedDataOperators operators, int i, IOrderedQueryable<ScriptAudit> orderedQuery,
            IQueryable<ScriptAudit> scriptAuditsQuery, Expression<Func<ScriptAudit, T>> expr)
        {
            if (i == 0)
                switch (operators.SortOrders[i].Direction)
                {
                    case "asc":
                        orderedQuery = scriptAuditsQuery.OrderBy(expr);
                        break;

                    case "desc":
                        orderedQuery = scriptAuditsQuery.OrderByDescending(expr);
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

        private static Expression<Func<ScriptAudit, R>> GetExpressionForOrdering<R>(MemberExpression prop, ParameterExpression param)
        {
            return Expression.Lambda<Func<ScriptAudit, R>>(prop, param);
        }
    }
}