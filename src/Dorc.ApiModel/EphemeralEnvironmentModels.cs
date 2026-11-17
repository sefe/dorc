using System;
using System.Collections.Generic;

namespace Dorc.ApiModel
{
    public static class TerraformDestroyOperationConstants
    {
        public const string PropertyName = "TerraformOperation";
        public const string DestroyValue = "Destroy";
        public const string IsDestroyPropertyName = "IsDestroy";
        public const string IsEphemeralPropertyName = "DOrcIsEphemeral";
        public const string StateStorageAccountPropertyName = "TerraformStateStorageAccount";
        public const string StateContainerNamePropertyName = "TerraformStateContainerName";
        public const string StateKeyPropertyName = "TerraformStateKey";
        public const string DeleteEnvironmentOnSuccessPropertyName = "DeleteEnvironmentOnTeardown";
        public const string DeferredValidationComponentsPropertyName = "DeferredValidationComponents";
        public const string DeferredValidationBuildNumPropertyName = "DeferredValidationBuildNum";
        public const string DeferredValidationBuildUrlPropertyName = "DeferredValidationBuildUrl";
        public const string EphemeralTargetStatePropertyName = "EphemeralTargetState";
        public const string DestroyOrderPreReversedPropertyName = "DOrcDestroyOrderPreReversed";
        public const string DestroyBatchCountPropertyName = "DOrcDestroyBatchCount";
        public const string SiblingDestroyRequestIdsPropertyName = "DOrcSiblingDestroyRequestIds";
        public const string DestroyComponentIdsPropertyName = "DOrcDestroyComponentIds";
        public const string EphemeralEnvironmentIdPropertyName = "EphemeralEnvironmentId";
        public const string RunValidationRequestPropertyName = "RunValidationRequest";

        public static readonly IReadOnlyCollection<string> ReservedLifecycleProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            PropertyName,
            IsDestroyPropertyName,
            IsEphemeralPropertyName,
            StateStorageAccountPropertyName,
            StateContainerNamePropertyName,
            StateKeyPropertyName,
            DeleteEnvironmentOnSuccessPropertyName,
            DeferredValidationComponentsPropertyName,
            DeferredValidationBuildNumPropertyName,
            DeferredValidationBuildUrlPropertyName,
            EphemeralTargetStatePropertyName,
            DestroyOrderPreReversedPropertyName,
            DestroyBatchCountPropertyName,
            SiblingDestroyRequestIdsPropertyName,
            DestroyComponentIdsPropertyName,
            EphemeralEnvironmentIdPropertyName,
            RunValidationRequestPropertyName
        };

        public static bool IsReservedLifecycleProperty(string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
                return false;
            var trimmed = propertyName.Trim();
            var colonIdx = trimmed.LastIndexOf(':');
            var bare = colonIdx >= 0 ? trimmed.Substring(colonIdx + 1).Trim() : trimmed;
            return ((HashSet<string>)ReservedLifecycleProperties).Contains(bare);
        }
    }

    public static class EphemeralEnvironmentStates
    {
        public const string Provisioning = "Provisioning";
        public const string Ready = "Ready";
        public const string Testing = "Testing";
        public const string TearingDown = "TearingDown";
        public const string TornDown = "TornDown";
        public const string TeardownFailed = "TeardownFailed";
        public const string ProvisionFailed = "ProvisionFailed";
        public const string ValidationFailed = "ValidationFailed";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Provisioning,
            Ready,
            Testing,
            TearingDown,
            TornDown,
            TeardownFailed,
            ProvisionFailed,
            ValidationFailed
        };

        public static bool IsValid(string state)
        {
            return state == Provisioning
                || state == Ready
                || state == Testing
                || state == TearingDown
                || state == TornDown
                || state == TeardownFailed
                || state == ProvisionFailed
                || state == ValidationFailed;
        }
    }

    public class EphemeralEnvironmentProvisionRequestApiModel
    {
        public int ProjectId { get; set; }
        public int? TemplateId { get; set; }
        public string EnvironmentName { get; set; }
        public string EnvironmentTier { get; set; }
        public int? TtlMinutes { get; set; }
        public int? BaseEnvironmentId { get; set; }
        public Dictionary<string, string> ParameterOverrides { get; set; } = new Dictionary<string, string>();
        public bool RunValidationComponents { get; set; }
        public string BuildNum { get; set; }
        public string BuildUrl { get; set; }
        public string BuildText { get; set; }
    }

    public class EphemeralEnvironmentProvisionResponseApiModel
    {
        public EnvironmentApiModel Environment { get; set; }
        public List<int> ProvisionRequestIds { get; set; } = new List<int>();
        public List<int> ValidationRequestIds { get; set; } = new List<int>();
        public Dictionary<string, Dictionary<string, string>> SeededComponentInputs { get; set; } = new Dictionary<string, Dictionary<string, string>>();
    }

    public class EphemeralEnvironmentTeardownRequestApiModel
    {
        public string Reason { get; set; }
        public bool ForceDeleteMetadataOnNoComponents { get; set; }
        public bool RetainMetadataAfterTeardown { get; set; }
    }

    public class EphemeralEnvironmentTeardownResponseApiModel
    {
        public string EnvironmentName { get; set; }
        public string EphemeralState { get; set; }
        public List<int> CancelledRequestIds { get; set; } = new List<int>();
        public int? DestroyRequestId { get; set; }
        public List<int> DestroyRequestIds { get; set; } = new List<int>();
        public bool EnvironmentDeleted { get; set; }
    }

    public class EphemeralEnvironmentExtendTtlRequestApiModel
    {
        public int? AdditionalMinutes { get; set; }
        public DateTime? NewExpiresAtUtc { get; set; }
    }

    public class EphemeralEnvironmentTierPreviewRequestApiModel
    {
        public int ProjectId { get; set; }
        public int? TemplateId { get; set; }
        public string EnvironmentName { get; set; }
        public string EnvironmentTier { get; set; }
        public int? BaseEnvironmentId { get; set; }
        public Dictionary<string, string> ParameterOverrides { get; set; } = new Dictionary<string, string>();
    }

    public class EphemeralEnvironmentTierPreviewResponseApiModel
    {
        public string EnvironmentTier { get; set; }
        public bool IsEphemeral { get; set; }
        public List<EphemeralComponentParameterPreviewApiModel> ComponentPreviews { get; set; } = new List<EphemeralComponentParameterPreviewApiModel>();
    }

    public class EphemeralComponentParameterPreviewApiModel
    {
        public int ComponentId { get; set; }
        public string ComponentName { get; set; }
        public string TemplateName { get; set; }
        public bool IsSharedService { get; set; }
        public Dictionary<string, string> ResolvedParameters { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> ForcedInvariants { get; set; } = new Dictionary<string, string>();
    }

    public class EnvironmentBlueprintDefinitionApiModel
    {
        public string SchemaVersion { get; set; } = "1.0";
        public string ProjectName { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string DefaultTier { get; set; } = "ephemeral";
        public string BaseEnvironmentName { get; set; }
        public int DefaultTtlMinutes { get; set; } = 240;
        public int MaxTtlMinutes { get; set; } = 1440;
        public List<string> ProvisionComponentNames { get; set; } = new List<string>();
        public List<string> ValidationComponentNames { get; set; } = new List<string>();
        public Dictionary<string, string> GlobalParameterOverrides { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Dictionary<string, string>> TierParameterOverrides { get; set; } = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
    }

    public class EnvironmentBlueprintImportRequestApiModel
    {
        public int? ProjectId { get; set; }
        public string ProjectName { get; set; }
        public List<EnvironmentBlueprintDefinitionApiModel> Blueprints { get; set; } = new List<EnvironmentBlueprintDefinitionApiModel>();
    }

    public class EnvironmentBlueprintImportResponseApiModel
    {
        public List<EnvironmentTemplateApiModel> ImportedTemplates { get; set; } = new List<EnvironmentTemplateApiModel>();
        public int CreatedCount { get; set; }
        public int UpdatedCount { get; set; }
    }

    public class EnvironmentBlueprintCloneRequestApiModel
    {
        public string NewName { get; set; }
        public string Description { get; set; }
    }

    public class EnvironmentBlueprintCaptureFromEnvironmentRequestApiModel
    {
        public int ProjectId { get; set; }
        public int BaseEnvironmentId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string DefaultTier { get; set; } = "ephemeral";
        public int DefaultTtlMinutes { get; set; } = 240;
        public int MaxTtlMinutes { get; set; } = 1440;
        public bool IncludeBaseEnvironmentSavedInputsAsOverrides { get; set; }
    }

    public class EnvironmentBlueprintParameterSchemaApiModel
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Description { get; set; }
        public bool Required { get; set; }
        public bool Sensitive { get; set; }
        public bool IsLockedSafetyInvariant { get; set; }
        public string DefaultValue { get; set; }
        public string BaseEnvironmentValue { get; set; }
        public List<string> AllowedValues { get; set; } = new List<string>();
        public Dictionary<string, string> TierSuggestedValues { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public class EnvironmentBlueprintComponentSchemaApiModel
    {
        public int ComponentId { get; set; }
        public string ComponentName { get; set; }
        public string ComponentType { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsCatalogTerraform { get; set; }
        public string TerraformTemplateName { get; set; }
        public string TerraformTemplateVersion { get; set; }
        public bool ManifestResolved { get; set; }
        public List<EnvironmentBlueprintParameterSchemaApiModel> Parameters { get; set; } = new List<EnvironmentBlueprintParameterSchemaApiModel>();
    }

    public class EnvironmentBlueprintHealthStatusApiModel
    {
        public int TemplateId { get; set; }
        public string TemplateName { get; set; }
        public bool IsHealthy { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class EnvironmentBlueprintSchemaContextApiModel
    {
        public int ProjectId { get; set; }
        public string ProjectName { get; set; }
        public int? BaseEnvironmentId { get; set; }
        public List<string> SupportedTiers { get; set; } = new List<string>();
        public List<EnvironmentBlueprintComponentSchemaApiModel> Components { get; set; } = new List<EnvironmentBlueprintComponentSchemaApiModel>();
        public List<EnvironmentBlueprintHealthStatusApiModel> TemplateHealth { get; set; } = new List<EnvironmentBlueprintHealthStatusApiModel>();
    }
}
