using Dorc.ApiModel;

namespace Dorc.Api.Validation
{
    // a Terraform component must either reference a stock
    // template from the catalog (TerraformTemplateName + TerraformTemplateVersion)
    // OR use a direct source (ScriptPath / TerraformSourceType) - never both.
    // Mixed configuration is ambiguous: the runner could not determine which
    // source to fetch. Reject at component-save time with a precise error
    // string so the engineer fixes the component definition before deploy.
    public static class TerraformExclusivityValidator
    {
        public sealed class ValidationException : ApplicationException
        {
            public ValidationException(string message) : base(message) { }
        }

        public static void Validate(ComponentApiModel component)
        {
            if (component is null) return;

            var hasCatalogRef = !string.IsNullOrEmpty(component.TerraformTemplateName)
                                 || !string.IsNullOrEmpty(component.TerraformTemplateVersion);
            var hasDirectSource = !string.IsNullOrEmpty(component.ScriptPath);

            if (hasCatalogRef && hasDirectSource)
            {
                throw new ValidationException(
                    $"Component '{component.ComponentName}' has both a Terraform catalog " +
                    $"reference (TerraformTemplateName='{component.TerraformTemplateName}', " +
                    $"TerraformTemplateVersion='{component.TerraformTemplateVersion}') and a " +
                    $"direct source (ScriptPath='{component.ScriptPath}'). These are mutually " +
                    "exclusive: remove either the template reference or the script path.");
            }

            if (hasCatalogRef)
            {
                // Both halves of the catalog reference are required when one
                // is supplied; otherwise the runner cannot resolve a manifest.
                if (string.IsNullOrEmpty(component.TerraformTemplateName))
                {
                    throw new ValidationException(
                        $"Component '{component.ComponentName}' specifies " +
                        "TerraformTemplateVersion but not TerraformTemplateName.");
                }
                if (string.IsNullOrEmpty(component.TerraformTemplateVersion))
                {
                    throw new ValidationException(
                        $"Component '{component.ComponentName}' specifies " +
                        "TerraformTemplateName but not TerraformTemplateVersion.");
                }
            }
        }

        public static void ValidateAll(IEnumerable<ComponentApiModel> components)
        {
            if (components is null) return;
            foreach (var component in components)
            {
                Validate(component);
                if (component.Children is not null)
                {
                    ValidateAll(component.Children);
                }
            }
        }
    }
}
