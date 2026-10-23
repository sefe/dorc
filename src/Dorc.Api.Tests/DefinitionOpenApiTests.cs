using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Dorc.Api.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Writers;
using Swashbuckle.AspNetCore.Swagger;

namespace Dorc.Api.Tests;

[TestClass]
public class DefinitionOpenApiTests
{
    [TestMethod]
    public void DefinitionContracts_ExposeRecursiveChildrenAndRevisionPreconditions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var environment = NSubstitute.Substitute.For<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
        environment.ApplicationName = typeof(RefDataController).Assembly.GetName().Name!;
        services.AddSingleton(environment);
        services.AddControllers().AddApplicationPart(typeof(RefDataController).Assembly)
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = null;
                options.JsonSerializerOptions.IncludeFields = true;
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "Dorc.Api", Version = "1.0" });
            options.EnableAnnotations();
        });
        using var provider = services.BuildServiceProvider();
        var document = provider.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        Assert.IsTrue(document.Components.Schemas["ComponentApiModel"].Properties.ContainsKey("Children"));
        Assert.IsTrue(document.Components.Schemas["RefDataApiModel"].Properties.ContainsKey("Revision"));
        Assert.IsTrue(document.Paths.ContainsKey("/DeploymentDefinitions/history"));
        var target = System.Environment.GetEnvironmentVariable("DORC_UPDATE_DEFINITION_OPENAPI");
        if (string.IsNullOrEmpty(target)) return;
        using var text = new StringWriter();
        document.SerializeAsV3(new OpenApiJsonWriter(text));
        var generated = JsonNode.Parse(text.ToString())!;
        var existing = JsonNode.Parse(File.ReadAllText(target))!;
        // Refresh the affected authoritative paths and their referenced schemas,
        // preserving unrelated security metadata and existing spec maintenance.
        foreach (var path in new[] { "/DeploymentDefinitions", "/DeploymentDefinitions/history", "/RefDataProjects/{projectId}", "/RefData", "/RefDataScripts/edit", "/Projects/{projectId}/terraform-state-backend-cutovers" })
            existing["paths"]![path] = generated["paths"]![path]!.DeepClone();
        foreach (var schema in new[]
        {
            "RefDataApiModel", "ProjectApiModel", "TerraformStateBackendApiModel", "TerraformBackendCutoverApiModel", "TerraformLegacyRequestRetirementApiModel", "TerraformReadinessApiModel",
            "ComponentApiModel", "ScriptApiModel", "DeploymentDefinitionApiModel",
            "GetDeploymentDefinitionsResponse", "DeploymentDefinitionHistoryApiModel", "GetDeploymentDefinitionHistoryResponse",
            "DeploymentResultApiModel", "DeploymentResultAttemptApiModel", "EnvironmentContentBuildsApiModel", "TerraformDeploymentReceiptApiModel"
        })
            existing["components"]!["schemas"]![schema] = generated["components"]!["schemas"]![schema]!.DeepClone();
        File.WriteAllText(target, existing.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
