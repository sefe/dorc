using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Security.Principal;
using log4net;
using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Extensions;
using Dorc.PersistentData.Contexts;

namespace Dorc.PersistentData.Sources
{
    public class ScriptsPersistentSource : IScriptsPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;
        private readonly ILog _logger;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        public ScriptsPersistentSource(
            IDeploymentContextFactory contextFactory,
            ILog logger,
            IClaimsPrincipalReader claimsPrincipalReader
            )
        {
            _logger = logger;
            _contextFactory = contextFactory;
            _claimsPrincipalReader = claimsPrincipalReader;
        }

        public GetScriptsListResponseDto GetScriptsByPage(int limit, int page, PagedDataOperators operators)
        {
            PagedModel<Script> output = null;
            using (var context = _contextFactory.GetContext())
            {
                var scriptsQuery = context.Scripts.Include(s => s.Components).ThenInclude(x => x.Projects).AsQueryable();

                if (operators.Filters != null && operators.Filters.Any())
                {
                    foreach (var pagedDataFilter in operators.Filters)
                    {
                        if (pagedDataFilter == null)
                            continue;
                        if (!string.IsNullOrEmpty(pagedDataFilter.Path) && !string.IsNullOrEmpty(pagedDataFilter.FilterValue))
                        {
                            scriptsQuery =
                                scriptsQuery.Where(scriptsQuery.ContainsExpression(pagedDataFilter.Path,
                                    pagedDataFilter.FilterValue));
                        }
                    }
                }

                if (operators.SortOrders != null && operators.SortOrders.Any())
                {
                    IOrderedQueryable<Script> orderedQuery = null;

                    for (var i = 0; i < operators.SortOrders.Count; i++)
                    {
                        if (operators.SortOrders[i] == null)
                            continue;
                        if (string.IsNullOrEmpty(operators.SortOrders[i].Path) ||
                            string.IsNullOrEmpty(operators.SortOrders[i].Direction))
                            continue;

                        var param = Expression.Parameter(typeof(Script), "Script");
                        var prop = Expression.PropertyOrField(param, operators.SortOrders[i].Path);

                        switch (prop.Type)
                        {
                            case Type boolType when boolType == typeof(bool):
                                {
                                    var expr = GetExpressionForOrdering<bool>(prop, param);
                                    orderedQuery = OrderScripts(operators, i, orderedQuery, scriptsQuery, expr);
                                    break;
                                }
                            case Type stringType when stringType == typeof(string):
                                {
                                    var expr = GetExpressionForOrdering<string>(prop, param);
                                    orderedQuery = OrderScripts(operators, i, orderedQuery, scriptsQuery, expr);
                                    break;
                                }
                        }
                    }

                    if (orderedQuery != null)
                        output = orderedQuery.AsNoTracking()
                            .Paginate(page, limit);
                }

                if (output == null)
                    output = scriptsQuery.AsNoTracking()
                        .OrderBy(s => s.Name)
                        .Paginate(page, limit);

                return new GetScriptsListResponseDto
                {
                    CurrentPage = output.CurrentPage,
                    TotalPages = output.TotalPages,
                    TotalItems = output.TotalItems,
                    Items = output.Items.Select(MapToScriptApiModel).ToList()
                };
            }
        }

        public ScriptApiModel GetScript(int id)
        {
            using (var context = _contextFactory.GetContext())
            {
                var script = context.Scripts.FirstOrDefault(s => s.Id == id);

                return MapToScriptApiModel(script);
            }
        }

        public bool UpdateScript(ScriptApiModel script, IPrincipal user)
        {
            using (var context = _contextFactory.GetContext())
            {
                var foundScript = context.Scripts.Include(s => s.Components).FirstOrDefault(s => s.Id == script.Id);

                if (foundScript == null)
                    return false;

                string username = _claimsPrincipalReader.GetUserFullDomainName(user);
                _logger.Warn(
                    $"Script {script.Name} {script.InstallScriptName} updated from {foundScript.Components.FirstOrDefault()?.IsEnabled} to {script.IsEnabled} by {username} at {DateTime.Now:o}");

                foundScript.Name = script.Name;
                foundScript.Path = script.Path;
                foundScript.NonProdOnly = script.NonProdOnly;
                foundScript.IsPathJSON = script.IsPathJSON;
                foundScript.PowerShellVersionNumber = script.PowerShellVersionNumber;
                foreach (var scriptComponent in foundScript.Components)
                {
                    scriptComponent.IsEnabled = script.IsEnabled;
                }

                context.SaveChanges();
                return true;
            }
        }

        private static ScriptApiModel MapToScriptApiModel(Script script)
        {
            if (script == null) return null;

            var isEnabled = false;

            if (script.Components != null)
            {
                foreach (var component in script.Components)
                {
                    isEnabled = component.IsEnabled ?? true;
                }
            }

            return new ScriptApiModel
            {
                Id = script.Id,
                Name = script.Name,
                Path = script.Path,
                NonProdOnly = script.NonProdOnly,
                IsPathJSON = script.IsPathJSON,
                IsEnabled = isEnabled,
                PowerShellVersionNumber = script.PowerShellVersionNumber,
                ProjectNames = script.Components?.SelectMany(s => s.Projects).Select(p => p.Name).ToList()
            };
        }

        private static IOrderedQueryable<Script> OrderScripts<T>(PagedDataOperators operators, int i, IOrderedQueryable<Script> orderedQuery,
            IQueryable<Script> scriptsQuery, Expression<Func<Script, T>> expr)
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

        private static Expression<Func<Script, R>> GetExpressionForOrdering<R>(MemberExpression prop, ParameterExpression param)
        {
            return Expression.Lambda<Func<Script, R>>(prop, param);
        }
    }
}