using System.Globalization;
using System.Text.RegularExpressions;
using Dorc.ApiModel.MonitorRunnerApi;

namespace Dorc.Terraform.Catalog;

public static class TerraformWorkloadTarget
{
    public static string Describe(TerraformTemplateManifest? manifest, IDictionary<string, VariableValue> properties,
        string? subscriptionId, Func<string, bool> isSensitive)
    {
        var targets = new List<string>();
        if (manifest?.RequiredProviders.Keys.Any(key => key.Contains("azur", StringComparison.OrdinalIgnoreCase)) == true &&
            !isSensitive(TerraformCloudTargeting.SubscriptionIdPropertyName) && Guid.TryParse(subscriptionId, out var subscription))
            targets.Add($"Azure subscription: {subscription:D}");
        if (manifest?.RequiredProviders.Keys.Any(key => key.Contains("aiven", StringComparison.OrdinalIgnoreCase)) == true)
        {
            foreach (var name in new[] { "project", "project_name", "service", "service_name" })
            {
                var parameter = manifest.Parameters.SingleOrDefault(parameter => parameter.Name == name);
                if (parameter == null || parameter.Sensitive || isSensitive(name)) continue;
                var value = properties.TryGetValue(name, out var supplied)
                    ? Convert.ToString(supplied.Value, CultureInfo.InvariantCulture) : parameter.Default;
                if (value != null && Regex.IsMatch(value, @"\A[a-zA-Z0-9_.-]{1,128}\z"))
                    targets.Add($"{name}: {value}");
            }
        }
        return targets.Count == 0
            ? "Not disclosed or unknown; inspect the plan and environment configuration."
            : string.Join("; ", targets);
    }
}
