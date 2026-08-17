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
            PagedModel<Script> output = null;
            using (var context = _contextFactory.GetContext())
            {
                var scriptsQuery = context.Scripts.Include(s => s.Components).ThenInclude(x => x.Projects).AsQueryable();

                if (operators.Filters != null && operators.Filters.Any())
                {
                    var filterLambdas = new List<Expression<Func<Script, bool>>>();
                    
                    foreach (var pagedDataFilter in operators.Filters)
                    {
                        if (pagedDataFilter == null)
                            continue;
                        if (!string.IsNullOrEmpty(pagedDataFilter.Path) && !string.IsNullOrEmpty(pagedDataFilter.FilterValue))
                        {
                            // Special handling for ProjectNames filter
                            if (pagedDataFilter.Path == "ProjectNames")
                            {
                                filterLambdas.Add(s => 
                                    s.Components.Any(c => 
                                        c.Projects.Any(p => p.Name.Contains(pagedDataFilter.FilterValue))));
                            }
                            else
                            {
                                filterLambdas.Add(scriptsQuery.ContainsExpression(pagedDataFilter.Path,
                                    pagedDataFilter.FilterValue));
                            }
                        }
                    }
                    
                    scriptsQuery = WhereAll(scriptsQuery, filterLambdas.ToArray());
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

        /// <summary>
        /// Sets or clears the recorded content hash for a script.
        ///
        /// Separate from <see cref="UpdateScript"/> deliberately. The recorded hash decides what
        /// a script is allowed to BE, and the general edit is where someone renames a script or
        /// changes its PowerShell version — folding the two together would let an unrelated edit
        /// silently re-baseline the content, which is precisely the state an attacker with
        /// script-edit rights would want to reach.
        ///
        /// Clearing (a null hash) means "re-record on next execution", not "stop verifying this
        /// one". There is no per-script opt-out, because a per-script opt-out is a per-script
        /// hole; withdrawing the control is an estate-wide setting.
        /// </summary>
        /// <returns>False when no such script exists, or the hash is not shaped like one.</returns>
        public bool RecordContentHash(int scriptId, string? contentHash, IPrincipal user)
        {
            var recording = string.IsNullOrWhiteSpace(contentHash)
                ? null
                : contentHash!.Trim().ToLowerInvariant();

            if (recording != null && !ScriptContentHash.IsWellFormed(recording))
            {
                // Refused where it is recorded rather than discovered as a mismatch on the next
                // deployment, when the report would say the script had changed and it had not.
                return false;
            }

            using (var context = _contextFactory.GetContext())
            {
                var foundScript = context.Scripts
                    .Include(s => s.Components).ThenInclude(c => c.Projects)
                    .FirstOrDefault(s => s.Id == scriptId);

                if (foundScript == null)
                    return false;

                if (string.Equals(foundScript.ContentHash, recording, StringComparison.Ordinal))
                {
                    // Already what it is being set to. Recording it again would add an audit
                    // entry saying nothing happened, and a pipeline that re-promotes an
                    // unchanged artefact would fill the audit trail with them.
                    return true;
                }

                var fromValue = $"ContentHash={foundScript.ContentHash ?? "(unrecorded)"}";
                var toValue = $"ContentHash={recording ?? "(unrecorded)"}";

                foundScript.ContentHash = recording;

                var projectNames = string.Join(", ", foundScript.Components
                    .SelectMany(c => c.Projects)
                    .Select(p => p.Name)
                    .Distinct());

                context.SaveChanges();

                // Audited like any other change to a script. A hash that changes without a
                // promotion behind it is exactly the thing an investigation would want to find,
                // and an unaudited security baseline is not a baseline.
                _scriptsAuditPersistentSource.AddRecord(
                    foundScript.Id, foundScript.Name, fromValue, toValue,
                    _claimsPrincipalReader.GetUserFullDomainName(user),
                    recording == null ? "ClearContentHash" : "RecordContentHash",
                    projectNames);

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
                ContentHash = script.ContentHash,
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

        private IQueryable<T> WhereAll<T>(
            IQueryable<T> source,
            params Expression<Func<T, bool>>[] predicates)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicates == null) throw new ArgumentNullException(nameof(predicates));
            if (predicates.Length == 0) return source; // no filters, return unfiltered
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
    }
}