using System.Text.Json;
using Dorc.Api.Interfaces;
using Dorc.Core.AzureDevOpsServer;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.Api.Services
{
    public class AzureDevOpsCrInputsProvider : ICrInputsProvider
    {
        private static readonly string[] CandidateFileNames = new[]
        {
            "cr-inputs-new.json",
            "cr-inputs.json",
        };

        private readonly IProjectsPersistentSource _projectsPersistentSource;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<AzureDevOpsCrInputsProvider> _logger;

        public AzureDevOpsCrInputsProvider(
            IProjectsPersistentSource projectsPersistentSource,
            ILoggerFactory loggerFactory,
            ILogger<AzureDevOpsCrInputsProvider> logger)
        {
            _projectsPersistentSource = projectsPersistentSource;
            _loggerFactory = loggerFactory;
            _logger = logger;
        }

        private static string Sanitize(string? value) =>
            (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);

        /// <inheritdoc />
        public async Task<CrInputsModel?> GetCrInputsAsync(string projectName)
        {
            if (string.IsNullOrWhiteSpace(projectName))
            {
                _logger.LogWarning("Cannot fetch cr-inputs.json: projectName is empty");
                return null;
            }

            var safeName = Sanitize(projectName);

            try
            {
                var project = _projectsPersistentSource.GetProject(projectName);
                if (project == null)
                {
                    _logger.LogWarning("Project '{ProjectName}' not found in DOrc database", safeName);
                    return null;
                }

                if (string.IsNullOrEmpty(project.ArtefactsUrl) || !project.ArtefactsUrl.StartsWith("http"))
                {
                    _logger.LogInformation("Project '{ProjectName}' has no Azure DevOps ArtefactsUrl", safeName);
                    return null;
                }

                if (string.IsNullOrEmpty(project.ArtefactsSubPaths))
                {
                    _logger.LogInformation("Project '{ProjectName}' has no ArtefactsSubPaths", safeName);
                    return null;
                }

                var adoClient = new AzureDevOpsServerWebClient(
                    project.ArtefactsUrl,
                    _loggerFactory.CreateLogger<AzureDevOpsServerWebClient>());

                var fileContent = await adoClient.GetFileFromRepoAsync(
                    project.ArtefactsUrl,
                    project.ArtefactsSubPaths,
                    CandidateFileNames);

                if (string.IsNullOrEmpty(fileContent))
                {
                    _logger.LogInformation("cr-inputs.json not found for project '{ProjectName}' (ADO: {AdoProjects})",
                        safeName, Sanitize(project.ArtefactsSubPaths));
                    return null;
                }

                return ParseCrInputs(fileContent, safeName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching cr-inputs.json for project '{ProjectName}'", safeName);
                return null;
            }
        }

        private CrInputsModel? ParseCrInputs(string json, string safeProjectName)
        {
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, options);
                if (raw == null)
                {
                    _logger.LogWarning("cr-inputs.json for project '{ProjectName}' parsed as null", safeProjectName);
                    return null;
                }

                var assignmentGroup = GetStringValue(raw, "assignment_group");
                if (string.IsNullOrEmpty(assignmentGroup))
                {
                    assignmentGroup = GetStringValue(raw, "SupportGroup");
                    if (!string.IsNullOrEmpty(assignmentGroup))
                        _logger.LogInformation("Using legacy 'SupportGroup' for project '{ProjectName}': '{Value}'",
                            safeProjectName, Sanitize(assignmentGroup));
                }

                var model = new CrInputsModel
                {
                    AssignmentGroup = assignmentGroup,
                    BusinessService = GetStringValue(raw, "business_service"),
                    ChgModel = GetStringValue(raw, "chg_model"),
                    Type = GetStringValue(raw, "type"),
                    ShortDescription = GetStringValue(raw, "short_description"),
                    BackoutPlan = GetStringValue(raw, "backout_plan"),
                    ImplementationPlan = GetStringValue(raw, "implementation_plan"),
                    Justification = GetStringValue(raw, "justification"),
                    TestPlan = GetStringValue(raw, "test_plan"),
                    RiskImpactAnalysis = GetStringValue(raw, "risk_impact_analysis"),
                    WorkNotes = GetStringValue(raw, "work_notes"),
                    Category = GetStringValue(raw, "category"),
                    CorrelationId = GetStringValue(raw, "correlation_id"),
                    Impact = GetStringValue(raw, "impact"),
                    Priority = GetStringValue(raw, "priority"),
                    Urgency = GetStringValue(raw, "urgency"),
                    ScheduledStartDate = GetStringValue(raw, "ScheduledStartDate"),
                    ScheduledEndDate = GetStringValue(raw, "ScheduledEndDate"),
                    BranchingStrategy = GetStringValue(raw, "BranchingStrategy"),
                    TeamProjectName = GetStringValue(raw, "TeamProjectName"),
                    IssuePathFilter = GetStringValue(raw, "IssuePathFilter"),
                };

                _logger.LogInformation("Parsed cr-inputs.json for '{ProjectName}': group='{Group}', service='{Service}'",
                    safeProjectName, Sanitize(model.AssignmentGroup), Sanitize(model.BusinessService));

                return model;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse cr-inputs.json for project '{ProjectName}'", safeProjectName);
                return null;
            }
        }

        private static string GetStringValue(Dictionary<string, JsonElement> dict, string key)
        {
            if (!dict.TryGetValue(key, out var element))
                return string.Empty;

            if (element.ValueKind == JsonValueKind.Null)
                return string.Empty;

            return element.GetString() ?? element.ToString() ?? string.Empty;
        }
    }
}
