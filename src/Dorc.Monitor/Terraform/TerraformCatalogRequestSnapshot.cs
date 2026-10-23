using System.Text.Json;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.PersistentData.Model;

namespace Dorc.Monitor.Terraform;

internal static class TerraformCatalogRequestSnapshot
{
    internal static IDictionary<string, VariableValue> Apply(ComponentApiModel component,
        IDictionary<string, VariableValue> inherited, string requestDetails, CatalogRequestInputs snapshot)
    {
        if (snapshot.ComponentId != component.ComponentId)
            throw new InvalidOperationException("Catalog input snapshot belongs to another component.");
        component.ComponentType = ComponentType.Terraform;
        component.TerraformSourceType = TerraformSourceType.Catalog;
        component.TerraformTemplateName = snapshot.TemplateName;
        component.TerraformTemplateVersion = snapshot.TemplateVersion;
        var explicitNames = new DeploymentRequestDetailSerializer().Deserialize(requestDetails)
            .Properties.Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        var properties = new Dictionary<string, VariableValue>(inherited, StringComparer.Ordinal);
        foreach (var input in JsonSerializer.Deserialize<Dictionary<string, string>>(snapshot.InputsJson)!)
            if (!explicitNames.Contains(input.Key))
                properties[input.Key] = new VariableValue { Value = input.Value, Type = typeof(string) };
        return properties;
    }
}
