using System.Collections.Generic;

namespace Dorc.ApiModel
{
    /// <summary>
    /// Request body for POST /Terraform/templates/{name}/{version}/instantiate.
    /// In **create-component-only** mode (the default) creates a Catalog-mode
    /// component in the destination project so the engineer can deploy the
    /// stock template through the existing DOrc deploy flow. In
    /// **create-and-deploy** mode (when EnvironmentName + Parameters
    /// are supplied) additionally composes and submits a deploy request,
    /// returning the new request ID alongside the created component.
    /// </summary>
    public class TerraformTemplateInstantiateRequestApiModel
    {
        /// <summary>Destination project ID (from the existing project picker).</summary>
        public int ProjectId { get; set; }

        /// <summary>Name to assign to the new component within the project. Defaults to the template name when empty.</summary>
        public string ComponentName { get; set; }

        /// <summary>Optional parent component ID; null for top-level placement.</summary>
        public int? ParentComponentId { get; set; }

        /// <summary>
        /// when set alongside Parameters, switches the endpoint to
        /// create-and-deploy mode. The destination environment must exist and
        /// the caller must have CanModifyEnvironment on it (enforced at the
        /// controller, not in RequestService). Null/empty selects
        /// create-component-only mode (the legacy behaviour).
        /// </summary>
        public string EnvironmentName { get; set; }

        /// <summary>
        /// manifest parameter values to deploy with. Keys must match
        /// manifest.Parameters[i].Name; values pass through ParameterValidator
        /// against the manifest's allow-list / regex / min-max rules. Required
        /// only when EnvironmentName is supplied.
        /// </summary>
        public Dictionary<string, string> Parameters { get; set; }
    }
}
