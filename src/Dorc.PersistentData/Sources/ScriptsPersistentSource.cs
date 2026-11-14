using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Extensions;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Security;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Data;
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
                var script = context.Scripts.Include(s => s.Components).ThenInclude(c => c.Projects).FirstOrDefault(s => s.Id == id);

                return MapToScriptApiModel(script);
            }
        }

        public bool UpdateScript(ScriptApiModel script, IPrincipal user)
        {
            var confinement = ScriptPathConfinement.Check(script.Path);
            if (!confinement.Allowed)
            {
                throw new ArgumentOutOfRangeException(nameof(script),
                    "Script '" + script.Name + "' has a path that cannot be accepted, because " + confinement.Reason);
            }

            using (var context = _contextFactory.GetContext())
            {
                var foundScript = context.Scripts.Include(s => s.Components).ThenInclude(c => c.Projects).FirstOrDefault(s => s.Id == script.Id);

                if (foundScript == null)
                    return false;

                string username = _claimsPrincipalReader.GetUserFullDomainName(user);

                // Capture old values for audit
                var oldApiModel = MapToScriptApiModel(foundScript);
                var oldIsEnabled = foundScript.Components.FirstOrDefault()?.IsEnabled ?? false;
                var previousContentHash = foundScript.ContentHash;
                var nextContentHash = ScriptContentHash.RetainForPath(previousContentHash, foundScript.Path, script.Path);
                var baselineCleared = !string.Equals(previousContentHash, nextContentHash, StringComparison.Ordinal);
                var projectNames = string.Join(", ", foundScript.Components
                    .SelectMany(c => c.Projects)
                    .Select(p => p.Name)
                    .Distinct());


                // Build from/to value strings for audit
                var fromValue = $"Name={oldApiModel?.Name}; Path={oldApiModel?.Path}; NonProdOnly={oldApiModel?.NonProdOnly}; IsEnabled={oldIsEnabled}; PSVersion={oldApiModel?.PowerShellVersionNumber}"
                    + (baselineCleared ? $"; ContentHash={previousContentHash}" : string.Empty);
                
                foundScript.Name = script.Name;
                foundScript.Path = script.Path;
                foundScript.NonProdOnly = script.NonProdOnly;
                foundScript.IsPathJSON = script.IsPathJSON;
                foundScript.PowerShellVersionNumber = script.PowerShellVersionNumber.ToSafePsVersionString();
                foundScript.ContentHash = nextContentHash;
                foreach (var scriptComponent in foundScript.Components)
                {
                    scriptComponent.IsEnabled = script.IsEnabled;
                }

                var toValue = $"Name={script.Name}; Path={script.Path}; NonProdOnly={script.NonProdOnly}; IsEnabled={script.IsEnabled}; PSVersion={script.PowerShellVersionNumber}"
                    + (baselineCleared ? "; ContentHash=(unrecorded)" : string.Empty);

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
            return RecordContentHash(
                scriptId,
                contentHash,
                expectedPath: null,
                user,
                projectAuthorize: null);
        }

        /// <inheritdoc />
        public bool RecordContentHash(
            int scriptId,
            string? contentHash,
            string? expectedPath,
            IPrincipal user,
            Func<int, bool>? projectAuthorize = null)
        {
            if (!ScriptContentHash.TryNormalize(contentHash, out var recording))
            {
                // Refused where it is recorded rather than discovered as a mismatch on the next
                // deployment, when the report would say the script had changed and it had not.
                return false;
            }

            using (var context = _contextFactory.GetContext())
            {
                var strategy = context.Database.CreateExecutionStrategy();

                return strategy.Execute(() =>
                {
                    context.ChangeTracker.Clear();

                    var useSqlServerLocking = context is DeploymentContext && context.Database.IsSqlServer();
                    using (var transaction = useSqlServerLocking
                        ? context.Database.BeginTransaction(IsolationLevel.Serializable)
                        : context.Database.BeginTransaction())
                    {
                        try
                        {
                            IQueryable<Script> scriptsQuery = useSqlServerLocking
                                ? context.Scripts.FromSqlRaw("SELECT * FROM [deploy].[Script] WITH (UPDLOCK,HOLDLOCK)")
                                : context.Scripts;

                            var foundScript = scriptsQuery
                                .Include(s => s.Components).ThenInclude(c => c.Projects)
                                .FirstOrDefault(s => s.Id == scriptId);

                            var referencingProjectIds = GetReferencingProjectIds(foundScript);
                            if (projectAuthorize != null
                                && (foundScript == null
                                    || referencingProjectIds.Length == 0
                                    || referencingProjectIds.Any(projectId => !projectAuthorize(projectId))))
                            {
                                throw new UnauthorizedAccessException(
                                    "The script content hash can only be recorded by Admins or users with write/owner rights across all projects referencing the script!");
                            }

                            if (foundScript == null)
                            {
                                transaction.Rollback();
                                return false;
                            }

                            if (!string.IsNullOrWhiteSpace(expectedPath)
                                && !string.Equals(foundScript.Path, expectedPath, StringComparison.Ordinal))
                            {
                                throw new InvalidOperationException(
                                    "The script path does not match the expected path for this content hash baseline.");
                            }

                            if (string.Equals(foundScript.ContentHash, recording, StringComparison.Ordinal))
                            {
                                // Already what it is being set to. Recording it again would add an audit
                                // entry saying nothing happened, and a pipeline that re-promotes an
                                // unchanged artefact would fill the audit trail with them.
                                transaction.Commit();
                                return true;
                            }

                            var fromValue = $"ContentHash={foundScript.ContentHash ?? "(unrecorded)"}";
                            var toValue = $"ContentHash={recording ?? "(unrecorded)"}";

                            foundScript.ContentHash = recording;

                            var projectNames = string.Join(", ", foundScript.Components
                                .SelectMany(c => c.Projects)
                                .Select(p => p.Name)
                                .Distinct());

                            // Audited in the same transaction on the same context so a baseline
                            // update and its audit trail either commit together or roll back together.
                            context.AuditScripts.Add(new AuditScript
                            {
                                ScriptId = foundScript.Id,
                                ScriptName = foundScript.Name,
                                FromValue = fromValue,
                                ToValue = toValue,
                                UpdatedBy = _claimsPrincipalReader.GetUserFullDomainName(user),
                                UpdatedDate = DateTime.Now,
                                Type = recording == null ? "ClearContentHash" : "RecordContentHash",
                                ProjectNames = projectNames
                            });

                            context.SaveChanges();
                            transaction.Commit();
                            return true;
                        }
                        catch
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                });
            }
        }

        /// <summary>
        /// Records a first-use baseline without giving concurrent deployments permission to
        /// replace one another. The conditional update is one database statement; the read
        /// afterwards deliberately returns whichever value is authoritative after that race.
        /// </summary>
        public string? RecordContentHashIfUnrecorded(
            int scriptId, string contentHash, IPrincipal user)
        {
            return RecordContentHashIfUnrecordedCore(scriptId, expectedPath: null, contentHash, user);
        }

        /// <inheritdoc />
        public string? RecordContentHashIfUnrecorded(
            int scriptId, string expectedPath, string contentHash, IPrincipal user)
        {
            if (string.IsNullOrWhiteSpace(expectedPath))
            {
                return null;
            }

            return RecordContentHashIfUnrecordedCore(scriptId, expectedPath, contentHash, user);
        }

        private string? RecordContentHashIfUnrecordedCore(
            int scriptId, string? expectedPath, string contentHash, IPrincipal user)
        {
            var recording = contentHash?.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(recording)
                || !ScriptContentHash.IsWellFormed(recording))
            {
                return null;
            }

            using (var context = _contextFactory.GetContext())
            {
                var strategy = context.Database.CreateExecutionStrategy();

                return strategy.Execute(() =>
                {
                    context.ChangeTracker.Clear();

                    using (var transaction = context.Database.BeginTransaction())
                    {
                        try
                        {
                            var recorded = expectedPath == null
                                ? context.RecordScriptContentHashIfUnrecorded(scriptId, recording) == 1
                                : context.RecordScriptContentHashIfUnrecorded(scriptId, expectedPath, recording) == 1;

                            var foundScript = context.Scripts
                                .AsNoTracking()
                                .Include(s => s.Components).ThenInclude(c => c.Projects)
                                .FirstOrDefault(s => s.Id == scriptId);

                            if (foundScript == null)
                            {
                                transaction.Rollback();
                                return null;
                            }

                            if (expectedPath != null && !string.Equals(foundScript.Path, expectedPath, StringComparison.Ordinal))
                            {
                                transaction.Rollback();
                                return null;
                            }

                            if (recorded)
                            {
                                var projectNames = string.Join(", ", foundScript.Components
                                    .SelectMany(c => c.Projects)
                                    .Select(p => p.Name)
                                    .Distinct());

                                context.AuditScripts.Add(new AuditScript
                                {
                                    ScriptId = foundScript.Id,
                                    ScriptName = foundScript.Name,
                                    FromValue = "ContentHash=(unrecorded)",
                                    ToValue = $"ContentHash={recording}",
                                    UpdatedBy = _claimsPrincipalReader.GetUserFullDomainName(user),
                                    UpdatedDate = DateTime.Now,
                                    Type = "RecordFirstContentHash",
                                    ProjectNames = projectNames
                                });

                                context.SaveChanges();
                            }

                            transaction.Commit();
                            return foundScript.ContentHash;
                        }
                        catch
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                });
            }
        }

        private static int[] GetReferencingProjectIds(Script? script)
        {
            if (script?.Components == null)
            {
                return Array.Empty<int>();
            }

            return script.Components
                .SelectMany(c => c.Projects)
                .Select(p => p.Id)
                .Distinct()
                .Order()
                .ToArray();
        }

        internal static ScriptApiModel MapToScriptApiModel(Script script)
        {
            if (script == null) return null;

            var isEnabled = script.Components?.Any(c => c.IsEnabled == true) == true;

            return new ScriptApiModel
            {
                Id = script.Id,
                Revision = DeploymentDefinitions.ScriptRevision(script),
                AffectedProjectIds = script.Components.SelectMany(c => c.Projects).Select(p => p.Id).Distinct().Order().ToArray(),
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