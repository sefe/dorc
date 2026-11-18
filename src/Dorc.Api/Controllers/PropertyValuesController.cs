using Dorc.Api.Interfaces;
using Dorc.Api.Services;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.Core.Interfaces;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System.Text;
using System.Text.RegularExpressions;

namespace Dorc.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("[controller]")]
    [Route("api/[controller]")]
    public class PropertyValuesController : ControllerBase
    {
        private readonly IPropertyValuesService _propertyValuesService;
        private readonly IPropertyValuesPersistentSource _propertyValuesPersistentSource;
        private readonly IVariableResolver _variableResolver;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly IVariableScopeOptionsResolver _variableScopeOptionsResolver;
        private readonly ISecurityPrivilegesChecker _securityPrivilegesChecker;
        private readonly IPropertyResolutionTracer _propertyResolutionTracer;
        private readonly IActiveDirectorySearcher _directorySearcher;
        private readonly ILogger<PropertyValuesController> _logger;

        public PropertyValuesController(IPropertyValuesService propertyValuesService,
            IPropertyValuesPersistentSource propertyValuesPersistentSource,
            [FromKeyedServices("VariableResolver")] IVariableResolver variableResolver,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            IVariableScopeOptionsResolver variableScopeOptionsResolver,
            IDirectorySearcherFactory directorySearcherFactory,
            ILogger<PropertyValuesController> logger,
            IPropertyResolutionTracer? propertyResolutionTracer = null,
            ISecurityPrivilegesChecker? securityPrivilegesChecker = null)
        {
            _variableScopeOptionsResolver = variableScopeOptionsResolver;
            _environmentsPersistentSource = environmentsPersistentSource;
            _variableResolver = variableResolver;
            _propertyValuesPersistentSource = propertyValuesPersistentSource;
            _propertyValuesService = propertyValuesService;
            _directorySearcher = directorySearcherFactory.GetEntraSearcher();
            _logger = logger;
            _propertyResolutionTracer = propertyResolutionTracer!;
            _securityPrivilegesChecker = securityPrivilegesChecker!;
        }

        /// <summary>
        /// Get property value scope options
        /// </summary>
        /// <param name="propertyValueScope"></param>
        /// <returns></returns>
        [HttpGet]
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<PropertyValueScopeOptionApiModel>))]
        [Route("ScopeOptions")]
        public IActionResult GetScopeOptions(string? propertyValueScope)
        {
            _propertyValuesPersistentSource.AddFilter(PropertyValueFilterTypes.EnvironmentPropertyFilterType, propertyValueScope);
            _variableResolver.SetPropertyValue(PropertyValueScopeOptionsFixed.EnvironmentName, propertyValueScope);
            var environment = _environmentsPersistentSource.GetEnvironment(propertyValueScope, User);
            if (environment is not null)
            {
                EnrichEnvironmentOwnerEmails(environment);
                try
                {
                    _variableScopeOptionsResolver.SetPropertyValues(_variableResolver, environment);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex,
                        "Unable to resolve scope options for environment ID {EnvironmentId}.",
                        environment.EnvironmentId);
                    return BadRequest(ex.Message);
                }
            }
            else if (string.IsNullOrWhiteSpace(propertyValueScope))
            {
                _variableScopeOptionsResolver.SetGlobalPropertyValues(_variableResolver, User);
            }

            var props = _variableResolver.LocalProperties();

            if (props == null)
            {
                return NotFound();
            }

            List<PropertyValueScopeOptionApiModel> output = props.Where(FilterOutNullAndEmpty)
                .Select(model => new PropertyValueScopeOptionApiModel
                {
                    ValueFilterScope = propertyValueScope,
                    ValueOption = "$" + model.Key + "$",
                    SampleResolvedValue = model.Value.Value
                }).ToList();

            return Ok(output);
        }

        private void EnrichEnvironmentOwnerEmails(EnvironmentApiModel environment)
        {
            try
            {
                var ownerIds = _environmentsPersistentSource.GetEnvironmentOwnerIds(environment.EnvironmentId);
                if (ownerIds.Count == 0)
                    return;

                var emails = new List<string>();
                foreach (var ownerId in ownerIds)
                {
                    try
                    {
                        var ownerData = _directorySearcher.GetUserDataById(ownerId);
                        if (!string.IsNullOrEmpty(ownerData?.Email))
                        {
                            emails.Add(ownerData.Email);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to resolve owner email for owner ID '{OwnerId}' in '{EnvironmentName}'",
                            ownerId, environment.EnvironmentName);
                    }
                }

                if (emails.Count > 0)
                {
                    environment.Details!.EnvironmentOwnerEmails = emails;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve owner emails for '{EnvironmentName}'",
                    environment.EnvironmentName);
            }
        }

        private bool FilterOutNullAndEmpty(KeyValuePair<string, VariableValue> arg)
        {
            if (string.IsNullOrWhiteSpace(arg.Key) || arg.Key.Contains(';'))
                return false;

            if (arg.Value == null)
                return false;

            if (!(arg.Value.Value is string str))
                return true;

            return string.Empty != str;
        }

        /// <summary>
        /// Get property values
        /// </summary>
        /// <param name="propertyName"></param>
        /// <param name="environmentName"></param>
        /// <returns></returns>
        [Produces(typeof(IQueryable<PropertyValueDto>))]
        [HttpGet]
        public IActionResult GetPropertyValues(string propertyName = null, string environmentName = null)
        {
            try
            {
                var propertyValues = _propertyValuesService.GetPropertyValues(propertyName, environmentName,
                    User)
                    .OrderBy(pv => !string.IsNullOrEmpty(pv.PropertyValueFilter))
                    .ThenBy(pv => pv.PropertyValueFilter)
                    .ToArray();
                if (!propertyValues.Any())
                {
                    return NotFound();
                }

                return Ok(propertyValues);
            }
            catch (NonEnoughRightsException e)
            {
                return StatusCode(StatusCodes.Status403Forbidden, e);
            }
            catch (Exception e)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, e);
            }
        }

        /// <summary>
        /// Download non-secure property values for an environment as CSV
        /// </summary>
        /// <param name="environmentName"></param>
        /// <returns></returns>
        [HttpGet("non-secure-export")]
        public IActionResult DownloadNonSecurePropertyValues(string environmentName)
        {
            if (string.IsNullOrWhiteSpace(environmentName))
            {
                return BadRequest("environmentName is required");
            }

            try
            {
                var propertyValues = _propertyValuesService
                    .GetPropertyValues(null, environmentName, User)
                    .Where(pv => !(pv.Property?.Secure ?? false))
                    .ToList();

                if (!propertyValues.Any())
                {
                    return NoContent();
                }

                var builder = new StringBuilder();
                builder.AppendLine("PropertyName,Value,Is Secure");

                foreach (var propertyValue in propertyValues)
                {
                    builder.AppendLine(
                        $"{EscapeCsv(propertyValue.Property?.Name ?? string.Empty)},{EscapeCsv(propertyValue.Value ?? string.Empty)},FALSE");
                }

                var bytes = Encoding.UTF8.GetBytes(builder.ToString());
                return File(bytes, "text/csv", $"{environmentName}-non-secure-variables.csv");
            }
            catch (NonEnoughRightsException e)
            {
                return StatusCode(StatusCodes.Status403Forbidden, e);
            }
            catch (Exception e)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, e);
            }
        }

        private static string EscapeCsv(string value)
        {
            var escaped = value.Replace("\"", "\"\"");
            return escaped.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
                ? $"\"{escaped}\""
                : escaped;
        }

        /// <summary>
        /// Edit property values
        /// </summary>
        /// <param name="propertyValuesToUpdate"></param>
        /// <returns></returns>
        [Produces(typeof(IEnumerable<Response>))]
        [HttpPut]
        public IActionResult PutPropertyValue(IEnumerable<PropertyValueDto> propertyValuesToUpdate)
        {
            return Ok(_propertyValuesService.PutPropertyValues(propertyValuesToUpdate, User));
        }

        /// <summary>
        /// Create property values
        /// </summary>
        /// <param name="propertyValuesToCreate"></param>
        /// <returns></returns>
        [Produces(typeof(IEnumerable<Response>))]
        [HttpPost]
        public IActionResult PostPropertyValue(IEnumerable<PropertyValueDto> propertyValuesToCreate)
        {
            return Ok(_propertyValuesService.PostPropertyValues(propertyValuesToCreate, User));
        }

        /// <summary>
        /// Delete property values
        /// </summary>
        /// <param name="propertyValuesToDelete"></param>
        /// <returns></returns>
        [Produces(typeof(IEnumerable<Response>))]
        [HttpDelete]
        public IActionResult DeletePropertyValue(IEnumerable<PropertyValueDto> propertyValuesToDelete)
        {
            return Ok(_propertyValuesService.DeletePropertyValues(propertyValuesToDelete, User));
        }

        /// <summary>
        /// Resolve property values and provenance trace for contextual preview without executing dynamic expressions
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<PropertyResolutionTraceApiModel>))]
        [HttpPost("ResolvePreview")]
        public IActionResult ResolvePreview([FromBody] PropertyResolutionPreviewRequestApiModel request)
        {
            if (request == null)
            {
                return BadRequest("request is required");
            }

            EnvironmentApiModel? environment = null;
            if (!string.IsNullOrWhiteSpace(request.EnvironmentName))
            {
                var envName = request.EnvironmentName.Trim();
                if (!_environmentsPersistentSource.EnvironmentExists(envName))
                {
                    return BadRequest($"Environment '{envName}' does not exist");
                }

                environment = _environmentsPersistentSource.GetEnvironment(envName, User);
                if (environment == null ||
                    !environment.UserEditable &&
                    !environment.IsOwner &&
                    !_securityPrivilegesChecker.CanModifyEnvironment(User, envName) &&
                    !_securityPrivilegesChecker.IsEnvironmentOwnerOrAdmin(User, envName) &&
                    !_securityPrivilegesChecker.CanReadSecrets(User, envName))
                {
                    return StatusCode(StatusCodes.Status403Forbidden);
                }

                EnrichEnvironmentOwnerEmails(environment);
            }

            var traces = _propertyResolutionTracer.TraceProperties(request, environment, User);
            return Ok(traces);
        }

        /// <summary>
        /// Get dependent property values that reference the specified property name ("Where Used" reverse dependency graph)
        /// </summary>
        /// <param name="propertyName"></param>
        /// <returns></returns>
        [SwaggerResponse(StatusCodes.Status200OK, Type = typeof(List<PropertyUsageReferenceApiModel>))]
        [HttpGet("WhereUsed")]
        public IActionResult GetWhereUsed(string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                return BadRequest("propertyName is required");
            }

            var targetPropertyName = propertyName.Trim();
            var references = new List<PropertyUsageReferenceApiModel>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var parser = new PropertyParser();
            var tokenRegex = new Regex(
                $@"(?<!\$)(?:\$\{{\s*{Regex.Escape(targetPropertyName)}\s*\}}|\${Regex.Escape(targetPropertyName)}\$)(?!\$)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            void TryAddCandidate(string? candidatePropertyName, string? filterScope, string? rawValue, bool isSecure)
            {
                if (isSecure ||
                    string.IsNullOrWhiteSpace(candidatePropertyName) ||
                    string.IsNullOrWhiteSpace(rawValue))
                {
                    return;
                }

                if (!ReferencesPropertyToken(rawValue, targetPropertyName, parser, tokenRegex))
                {
                    return;
                }

                var normalizedScope = string.IsNullOrWhiteSpace(filterScope) ? "Default" : filterScope.Trim();
                var dedupKey = $"{candidatePropertyName.Trim()}|{normalizedScope}";
                if (!seen.Add(dedupKey))
                {
                    return;
                }

                references.Add(new PropertyUsageReferenceApiModel
                {
                    PropertyName = candidatePropertyName.Trim(),
                    Scope = normalizedScope,
                    PropertyValueFilter = string.IsNullOrWhiteSpace(filterScope) ? string.Empty : filterScope.Trim(),
                    RawValueSnippet = BuildRawValueSnippet(rawValue)
                });
            }

            var globalProperties = _propertyValuesPersistentSource.GetGlobalProperties();
            if (globalProperties != null)
            {
                foreach (var prop in globalProperties)
                {
                    TryAddCandidate(
                        prop?.Property?.Name,
                        prop?.PropertyValueFilter,
                        prop?.Value,
                        prop?.Property?.Secure ?? false);
                }
            }

            const int pageSize = 500;
            const int maxPages = 10;
            var filterOperators = new PagedDataOperators
            {
                Filters = new List<PagedDataFilter>
                {
                    new PagedDataFilter
                    {
                        Path = "PropertyValue",
                        FilterValue = targetPropertyName
                    }
                }
            };

            for (var page = 1; page <= maxPages; page++)
            {
                var pagedSearch = _propertyValuesPersistentSource.GetPropertyValuesForSearchValueByPage(
                    pageSize,
                    page,
                    filterOperators,
                    User);
                if (pagedSearch?.Items == null || pagedSearch.Items.Count == 0)
                {
                    break;
                }

                foreach (var item in pagedSearch.Items)
                {
                    TryAddCandidate(
                        item?.Property,
                        item?.PropertyValueScope,
                        item?.PropertyValue,
                        item?.Secure ?? false);
                }

                if (pagedSearch.Items.Count < pageSize ||
                    (pagedSearch.TotalPages > 0 && page >= pagedSearch.TotalPages))
                {
                    break;
                }
            }

            var orderedReferences = references
                .OrderBy(r => !string.Equals(r.Scope, "Default", StringComparison.OrdinalIgnoreCase))
                .ThenBy(r => r.Scope, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.PropertyName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Ok(orderedReferences);
        }

        private static bool ReferencesPropertyToken(
            string rawValue,
            string targetPropertyName,
            PropertyParser parser,
            Regex tokenRegex)
        {
            try
            {
                foreach (var token in parser.Parse(rawValue))
                {
                    if (token is PropertyToken propertyToken &&
                        string.Equals(propertyToken.Value, targetPropertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // Fallback to regex matching when value contains ${Var} brace syntax or unescaped literal $
            }

            var withoutEscapedDollarPairs = rawValue.Replace("$$", string.Empty);
            return tokenRegex.IsMatch(withoutEscapedDollarPairs);
        }

        private static string BuildRawValueSnippet(string rawValue)
        {
            const int maxSnippetLength = 120;
            var trimmed = rawValue.Trim();
            return trimmed.Length <= maxSnippetLength
                ? trimmed
                : trimmed.Substring(0, maxSnippetLength - 3) + "...";
        }
    }
}