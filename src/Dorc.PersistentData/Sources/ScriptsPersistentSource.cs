using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Extensions;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Security.Principal;

namespace Dorc.PersistentData.Sources
{
    public class ScriptsPersistentSource : IScriptsPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;
        private readonly IScriptsAuditPersistentSource _scriptsAuditPersistentSource;

        public ScriptsPersistentSource(
            IDeploymentContextFactory contextFactory,
            IClaimsPrincipalReader claimsPrincipalReader,
            IScriptsAuditPersistentSource scriptsAuditPersistentSource
            )
        {
            _contextFactory = contextFactory;
            _claimsPrincipalReader = claimsPrincipalReader;
            _scriptsAuditPersistentSource = scriptsAuditPersistentSource;
        }

        public GetScriptsListResponseDto GetScriptsByPage(int limit, int page, PagedDataOperators operators)
        {
            using var context = _contextFactory.GetContext();

            var scriptsQuery = context.Scripts.Include(s => s.Components).ThenInclude(x => x.Projects).AsQueryable();
            scriptsQuery = ApplyFilters(scriptsQuery, operators.Filters);

            var output = ApplySorting(scriptsQuery, operators.SortOrders)
                         ?? scriptsQuery.AsNoTracking().OrderBy(s => s.Name).Paginate(page, limit);

            return new GetScriptsListResponseDto
            {
                CurrentPage = output.CurrentPage,
                TotalPages = output.TotalPages,
                TotalItems = output.TotalItems,
                Items = output.Items.Select(MapToScriptApiModel).ToList()
            };

            PagedModel<Script>? ApplySorting(IQueryable<Script> query, List<PagedDataSorting>? sortOrders)
            {
                if (sortOrders is not { Count: > 0 })
                    return null;

                var orderedQuery = BuildOrderedQuery(query, sortOrders);
                return orderedQuery?.AsNoTracking().Paginate(page, limit);
            }
        }

        private static IQueryable<Script> ApplyFilters(IQueryable<Script> query, List<PagedDataFilter>? filters)
        {
            if (filters is not { Count: > 0 })
                return query;

            var filterLambdas = filters
                .Where(f => f != null && !string.IsNullOrEmpty(f.Path) && !string.IsNullOrEmpty(f.FilterValue))
                .Select(f => BuildFilterExpression(query, f))
                .ToList();

            return WhereAll(query, filterLambdas.ToArray());
        }

        private static Expression<Func<Script, bool>> BuildFilterExpression(IQueryable<Script> query, PagedDataFilter filter)
        {
            if (filter.Path == "ProjectNames")
            {
                return s => s.Components.Any(c => c.Projects.Any(p => p.Name.Contains(filter.FilterValue)));
            }

            return query.ContainsExpression(filter.Path, filter.FilterValue);
        }

        private static IOrderedQueryable<Script>? BuildOrderedQuery(IQueryable<Script> query, List<PagedDataSorting> sortOrders)
        {
            IOrderedQueryable<Script>? orderedQuery = null;

            for (var i = 0; i < sortOrders.Count; i++)
            {
                if (sortOrders[i] == null
                    || string.IsNullOrEmpty(sortOrders[i].Path)
                    || string.IsNullOrEmpty(sortOrders[i].Direction))
                    continue;

                var param = Expression.Parameter(typeof(Script), "Script");
                var prop = Expression.PropertyOrField(param, sortOrders[i].Path);

                orderedQuery = prop.Type switch
                {
                    Type t when t == typeof(bool) =>
                        OrderScripts(sortOrders, i, orderedQuery, query, GetExpressionForOrdering<bool>(prop, param)),
                    Type t when t == typeof(string) =>
                        OrderScripts(sortOrders, i, orderedQuery, query, GetExpressionForOrdering<string>(prop, param)),
                    _ => orderedQuery
                };
            }

            return orderedQuery;
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
                var foundScript = context.Scripts.Include(s => s.Components).ThenInclude(c => c.Projects).FirstOrDefault(s => s.Id == script.Id);

                if (foundScript == null)
                    return false;

                string username = _claimsPrincipalReader.GetUserFullDomainName(user);

                // Capture old values for audit
                var oldApiModel = MapToScriptApiModel(foundScript);
                var oldIsEnabled = foundScript.Components.FirstOrDefault()?.IsEnabled ?? false;
                var projectNames = string.Join(", ", foundScript.Components
                    .SelectMany(c => c.Projects)
                    .Select(p => p.Name)
                    .Distinct());


                // Build from/to value strings for audit
                var fromValue = $"Name={oldApiModel?.Name}; Path={oldApiModel?.Path}; NonProdOnly={oldApiModel?.NonProdOnly}; IsEnabled={oldIsEnabled}; PSVersion={oldApiModel?.PowerShellVersionNumber}";
                
                foundScript.Name = script.Name;
                foundScript.Path = script.Path;
                foundScript.NonProdOnly = script.NonProdOnly;
                foundScript.IsPathJSON = script.IsPathJSON;
                foundScript.PowerShellVersionNumber = script.PowerShellVersionNumber.ToSafePsVersionString();
                foreach (var scriptComponent in foundScript.Components)
                {
                    scriptComponent.IsEnabled = script.IsEnabled;
                }

                var toValue = $"Name={script.Name}; Path={script.Path}; NonProdOnly={script.NonProdOnly}; IsEnabled={script.IsEnabled}; PSVersion={script.PowerShellVersionNumber}";

                context.SaveChanges();

                _scriptsAuditPersistentSource.AddRecord(
                    script.Id, script.Name, fromValue, toValue,
                    username, "Update", projectNames);

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
                    isEnabled = component.IsEnabled == true;
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

        private static IOrderedQueryable<Script> OrderScripts<T>(List<PagedDataSorting> sortOrders, int i, IOrderedQueryable<Script> orderedQuery,
            IQueryable<Script> scriptsQuery, Expression<Func<Script, T>> expr)
        {
            if (i == 0)
                switch (sortOrders[i].Direction)
                {
                    case "asc":
                        orderedQuery = scriptsQuery.OrderBy(expr);
                        break;

                    case "desc":
                        orderedQuery = scriptsQuery.OrderByDescending(expr);
                        break;
                }
            else
                switch (sortOrders[i].Direction)
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

        private static IQueryable<T> WhereAll<T>(
            IQueryable<T> source,
            params Expression<Func<T, bool>>[] predicates)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicates == null) throw new ArgumentNullException(nameof(predicates));
            if (predicates.Length == 0) return source;
            if (predicates.Length == 1) return source.Where(predicates[0]);

            Expression<Func<T, bool>> pred = null;
            for (var i = 0; i < predicates.Length; i++)
            {
                pred = pred == null
                    ? predicates[i]
                    : pred.And(predicates[i]);
            }
            return source.Where(pred);
        }
    }
}