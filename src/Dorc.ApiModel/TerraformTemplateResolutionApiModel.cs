using System.Collections.Generic;

namespace Dorc.ApiModel
{
    /// <summary>
    /// Where a manifest input would be resolved from if a plan were requested
    /// now for a given project and environment. Names only: the wizard needs to
    /// know whether an inherited value exists, never what it is.
    /// </summary>
    public enum TerraformParameterResolutionStatus
    {
        /// <summary>An environment-scoped DOrc property supplies the value.</summary>
        Environment = 0,

        /// <summary>No environment property; the manifest default applies.</summary>
        Default = 1,

        /// <summary>Required input with neither an environment property nor a default.</summary>
        Missing = 2,

        /// <summary>Optional input with neither an environment property nor a default.</summary>
        Unset = 3
    }

    public class TerraformParameterResolutionApiModel
    {
        public string Name { get; set; } = string.Empty;
        public bool Required { get; set; }
        public bool Sensitive { get; set; }
        public TerraformParameterResolutionStatus Status { get; set; }
    }

    /// <summary>
    /// Response body for GET /Terraform/templates/{name}/{version}/resolution.
    /// </summary>
    public class TerraformTemplateResolutionApiModel
    {
        public string ProjectName { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = string.Empty;
        public List<TerraformParameterResolutionApiModel> Parameters { get; set; } = new List<TerraformParameterResolutionApiModel>();
    }
}
