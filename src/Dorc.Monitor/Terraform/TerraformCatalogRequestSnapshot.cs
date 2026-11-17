using System.Text.Json;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.PersistentData.Model;

namespace Dorc.Monitor.Terraform;

internal static class TerraformCatalogRequestSnapshot
{
    internal static IDictionary<string, VariableValue> Apply(
        ComponentApiModel component,
        IDictionary<string, VariableValue> inherited,
        string? requestDetails,
        CatalogRequestInputs? snapshot,
        IEnumerable<RequestProperty>? fallbackRequestProperties = null)
    {
        if (snapshot is not null)
        {
            if (snapshot.ComponentId != component.ComponentId)
                throw new InvalidOperationException("Catalog input snapshot belongs to another component.");
            component.ComponentType = ComponentType.Terraform;
            component.TerraformSourceType = TerraformSourceType.Catalog;
            component.TerraformTemplateName = snapshot.TemplateName;
            component.TerraformTemplateVersion = snapshot.TemplateVersion;
        }

        var properties = new Dictionary<string, VariableValue>(inherited, StringComparer.Ordinal);
        var explicitNames = new HashSet<string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(requestDetails))
        {
            foreach (var prop in new DeploymentRequestDetailSerializer().Deserialize(requestDetails).Properties)
                ApplyRequestProperty(component.ComponentName, prop.Name, prop.Value, properties, explicitNames, setUnqualifiedValue: false, markExplicit: fallbackRequestProperties is null);
        }

        if (fallbackRequestProperties is not null)
        {
            foreach (var prop in fallbackRequestProperties)
                ApplyRequestProperty(component.ComponentName, prop.PropertyName, prop.PropertyValue, properties, explicitNames, setUnqualifiedValue: true, markExplicit: true);
        }

        if (snapshot is not null)
        {
            foreach (var input in JsonSerializer.Deserialize<Dictionary<string, string>>(snapshot.InputsJson)!)
                if (!explicitNames.Contains(input.Key))
                    properties[input.Key] = new VariableValue { Value = input.Value, Type = typeof(string) };
        }
        return properties;
    }

    private static void ApplyRequestProperty(
        string? componentName,
        string propertyName,
        string propertyValue,
        IDictionary<string, VariableValue> properties,
        ISet<string> explicitNames,
        bool setUnqualifiedValue,
        bool markExplicit)
    {
        if (TerraformDestroyOperationConstants.IsReservedLifecycleProperty(propertyName))
            return;

        var colonIndex = propertyName.LastIndexOf(':');
        if (colonIndex >= 0)
        {
            var targetComponent = propertyName[..colonIndex];
            var paramName = propertyName[(colonIndex + 1)..];
            if (string.Equals(targetComponent, componentName, StringComparison.OrdinalIgnoreCase))
            {
                properties[paramName] = new VariableValue { Value = propertyValue, Type = typeof(string) };
                if (markExplicit)
                    explicitNames.Add(paramName);
            }
            return;
        }

        if (setUnqualifiedValue)
            properties[propertyName] = new VariableValue { Value = propertyValue, Type = typeof(string) };
        if (markExplicit)
            explicitNames.Add(propertyName);
    }
}
