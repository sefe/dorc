using System.Net;
using System.Net.Http.Json;
using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Dorc.Api.Tests.Controllers;

[TestClass]
[TestCategory("Database")]
[DoNotParallelize]
public class RefDataEnvironmentsDetailsControllerTests
{
    private static DorcApiFixture _fixture = null!;

    [ClassInitialize]
    public static void Initialize(TestContext _) => _fixture = new DorcApiFixture("dorc_api_env_prop");

    [ClassCleanup]
    public static void Cleanup() => _fixture?.Dispose();

    [TestMethod]
    public async Task DualSchemeAuthentication_AdmitsBearerAndNegotiate_AndRejectsUnauthenticatedRequests()
    {
        using var anonymousClient = _fixture.HostFactory.CreateClient();
        var unauthMetaResp = await anonymousClient.GetAsync("/Metadata");
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthMetaResp.StatusCode);

        var unauthEnvResp = await anonymousClient.GetAsync("/RefDataEnvironments");
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthEnvResp.StatusCode);

        using var bearerClient = _fixture.CreateClientForUser("admin-oid", "admin.user@sefe.eu", "Admin", "Bearer");
        var metaResp = await bearerClient.GetAsync("/Metadata");
        Assert.AreEqual(HttpStatusCode.OK, metaResp.StatusCode);
        var versionText = await metaResp.Content.ReadAsStringAsync();
        Assert.IsFalse(string.IsNullOrWhiteSpace(versionText));

        var bearerRolesResp = await bearerClient.GetFromJsonAsync<List<string>>("/RefDataRoles", _fixture.JsonOptions);
        Assert.IsNotNull(bearerRolesResp);
        CollectionAssert.Contains(bearerRolesResp, "Admin");

        using var winAuthClient = _fixture.CreateClientForUser("win-sid", "win.user@sefe.eu", "Admin", "Negotiate");
        winAuthClient.DefaultRequestHeaders.Add("X-Test-Win-User", @"SEFE\svc-dorc-win");
        var winEnvResp = await winAuthClient.GetAsync("/RefDataEnvironments");
        Assert.AreEqual(HttpStatusCode.OK, winEnvResp.StatusCode);
    }

    [TestMethod]
    public async Task EnvironmentHierarchy_ParentChildAttachment_PropertyInheritance_AndCycleRejection_WorkEndToEnd()
    {
        using var adminClient = _fixture.CreateClientForUser("admin-oid", "admin.user@sefe.eu", "Admin");

        var parentName = "EnvParent_" + Guid.NewGuid().ToString("N")[..8];
        var childName = "EnvChild_" + Guid.NewGuid().ToString("N")[..8];

        var createParentResp = await adminClient.PostAsJsonAsync("/RefDataEnvironments", new EnvironmentApiModel
        {
            EnvironmentName = parentName,
            EnvironmentIsProd = false,
            EnvironmentSecure = false,
            Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "admin.user@sefe.eu", Description = "Parent env" }
        }, _fixture.JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, createParentResp.StatusCode);
        var parentEnv = await createParentResp.Content.ReadFromJsonAsync<EnvironmentApiModel>(_fixture.JsonOptions);
        Assert.IsNotNull(parentEnv);
        Assert.IsTrue(parentEnv.EnvironmentId > 0);

        var createChildResp = await adminClient.PostAsJsonAsync("/RefDataEnvironments", new EnvironmentApiModel
        {
            EnvironmentName = childName,
            EnvironmentIsProd = false,
            EnvironmentSecure = false,
            Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "admin.user@sefe.eu", Description = "Child env" }
        }, _fixture.JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, createChildResp.StatusCode);
        var childEnv = await createChildResp.Content.ReadFromJsonAsync<EnvironmentApiModel>(_fixture.JsonOptions);
        Assert.IsNotNull(childEnv);
        Assert.IsTrue(childEnv.EnvironmentId > 0);

        var invalidCreateWithParent = await adminClient.PostAsJsonAsync("/RefDataEnvironments", new EnvironmentApiModel
        {
            EnvironmentName = "InvalidParentOnCreate_" + Guid.NewGuid().ToString("N")[..6],
            ParentId = parentEnv.EnvironmentId,
            Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "admin.user@sefe.eu" }
        }, _fixture.JsonOptions);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalidCreateWithParent.StatusCode);

        var setParentResp = await adminClient.PutAsync(
            $"/RefDataEnvironmentsDetails/SetParentForEnvironment?parentEnvId={parentEnv.EnvironmentId}&childEnvId={childEnv.EnvironmentId}",
            null);
        Assert.AreEqual(HttpStatusCode.OK, setParentResp.StatusCode);
        var setParentResult = await setParentResp.Content.ReadFromJsonAsync<ApiBoolResult>(_fixture.JsonOptions);
        Assert.IsNotNull(setParentResult);
        Assert.IsTrue(setParentResult.Result, setParentResult.Message);

        var cycleResp = await adminClient.PutAsync(
            $"/RefDataEnvironmentsDetails/SetParentForEnvironment?parentEnvId={childEnv.EnvironmentId}&childEnvId={parentEnv.EnvironmentId}",
            null);
        var cycleResult = await cycleResp.Content.ReadFromJsonAsync<ApiBoolResult>(_fixture.JsonOptions);
        Assert.IsNotNull(cycleResult);
        Assert.IsFalse(cycleResult.Result);

        var grandChildResp = await adminClient.PostAsJsonAsync("/RefDataEnvironments", new EnvironmentApiModel
        {
            EnvironmentName = "EnvGrandChild_" + Guid.NewGuid().ToString("N")[..8],
            EnvironmentIsProd = false,
            EnvironmentSecure = false,
            Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "admin.user@sefe.eu" }
        }, _fixture.JsonOptions);
        var grandChildEnv = await grandChildResp.Content.ReadFromJsonAsync<EnvironmentApiModel>(_fixture.JsonOptions);
        Assert.IsNotNull(grandChildEnv);

        var childAsParentResp = await adminClient.PutAsync(
            $"/RefDataEnvironmentsDetails/SetParentForEnvironment?parentEnvId={childEnv.EnvironmentId}&childEnvId={grandChildEnv.EnvironmentId}",
            null);
        var childAsParentResult = await childAsParentResp.Content.ReadFromJsonAsync<ApiBoolResult>(_fixture.JsonOptions);
        Assert.IsNotNull(childAsParentResult);
        Assert.IsFalse(childAsParentResult.Result);
        StringAssert.Contains(childAsParentResult.Message, "Not allowed to have more than 1 level of environment hierarchy");

        var parentAsChildResp = await adminClient.PutAsync(
            $"/RefDataEnvironmentsDetails/SetParentForEnvironment?parentEnvId={grandChildEnv.EnvironmentId}&childEnvId={parentEnv.EnvironmentId}",
            null);
        var parentAsChildResult = await parentAsChildResp.Content.ReadFromJsonAsync<ApiBoolResult>(_fixture.JsonOptions);
        Assert.IsNotNull(parentAsChildResult);
        Assert.IsFalse(parentAsChildResult.Result);
        StringAssert.Contains(parentAsChildResult.Message, "cannot have both parent and children");

        var inheritedPropName = "InheritedProp_" + Guid.NewGuid().ToString("N")[..8];
        var overriddenPropName = "OverriddenProp_" + Guid.NewGuid().ToString("N")[..8];
        var secureArrayPropName = "SecureArrayProp_" + Guid.NewGuid().ToString("N")[..8];

        var createPropsResp = await adminClient.PostAsJsonAsync("/Properties", new[]
        {
            new PropertyApiModel { Name = inheritedPropName, Secure = false, IsArray = false },
            new PropertyApiModel { Name = overriddenPropName, Secure = false, IsArray = false },
            new PropertyApiModel { Name = secureArrayPropName, Secure = true, IsArray = true }
        }, _fixture.JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, createPropsResp.StatusCode);

        var postValuesResp = await adminClient.PostAsJsonAsync("/PropertyValues", new[]
        {
            new PropertyValueDto
            {
                Property = new PropertyApiModel { Name = inheritedPropName, Secure = false },
                PropertyValueFilter = parentName,
                Value = "from-parent"
            },
            new PropertyValueDto
            {
                Property = new PropertyApiModel { Name = overriddenPropName, Secure = false },
                PropertyValueFilter = parentName,
                Value = "parent-default"
            },
            new PropertyValueDto
            {
                Property = new PropertyApiModel { Name = overriddenPropName, Secure = false },
                PropertyValueFilter = childName,
                Value = "child-override"
            },
            new PropertyValueDto
            {
                Property = new PropertyApiModel { Name = secureArrayPropName, Secure = true, IsArray = true },
                PropertyValueFilter = childName,
                Value = "[\"sec1\",\"sec2\"]"
            }
        }, _fixture.JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, postValuesResp.StatusCode);

        var childInheritedResp = await adminClient.GetFromJsonAsync<PropertyValueDto[]>(
            $"/PropertyValues?propertyName={inheritedPropName}&environmentName={childName}", _fixture.JsonOptions);
        Assert.IsNotNull(childInheritedResp);
        Assert.AreEqual(1, childInheritedResp.Length);
        Assert.AreEqual("from-parent", childInheritedResp[0].Value);
        Assert.AreEqual(parentName, childInheritedResp[0].PropertyValueFilter);

        var childOverrideResp = await adminClient.GetFromJsonAsync<PropertyValueDto[]>(
            $"/PropertyValues?propertyName={overriddenPropName}&environmentName={childName}", _fixture.JsonOptions);
        Assert.IsNotNull(childOverrideResp);
        Assert.AreEqual(1, childOverrideResp.Length);
        Assert.AreEqual("child-override", childOverrideResp[0].Value);
        Assert.AreEqual(childName, childOverrideResp[0].PropertyValueFilter);

        var csvExportResp = await adminClient.GetAsync($"/PropertyValues/non-secure-export?environmentName={childName}");
        Assert.AreEqual(HttpStatusCode.OK, csvExportResp.StatusCode);
        var csvText = await csvExportResp.Content.ReadAsStringAsync();
        StringAssert.Contains(csvText, inheritedPropName);
        StringAssert.Contains(csvText, overriddenPropName);
        Assert.IsFalse(csvText.Contains(secureArrayPropName, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(csvText.Contains("sec1", StringComparison.OrdinalIgnoreCase));

        var updateSecureArrayResp = await adminClient.PutAsJsonAsync("/PropertyValues", new[]
        {
            new PropertyValueDto
            {
                Property = new PropertyApiModel { Name = secureArrayPropName, Secure = true, IsArray = true },
                PropertyValueFilter = childName,
                Value = "[\"sec3\",\"sec4\"]"
            }
        }, _fixture.JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, updateSecureArrayResp.StatusCode);

        var secureArrayReadResp = await adminClient.GetFromJsonAsync<PropertyValueDto[]>(
            $"/PropertyValues?propertyName={secureArrayPropName}&environmentName={childName}", _fixture.JsonOptions);
        Assert.IsNotNull(secureArrayReadResp);
        Assert.AreEqual(1, secureArrayReadResp.Length);
        Assert.AreEqual("[\"sec3\",\"sec4\"]", secureArrayReadResp[0].Value);

        var toggleIsArrayResp = await adminClient.PutAsJsonAsync("/Properties", new Dictionary<string, PropertyApiModel>
        {
            [secureArrayPropName] = new PropertyApiModel { Name = secureArrayPropName, Secure = true, IsArray = false }
        }, _fixture.JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, toggleIsArrayResp.StatusCode);
        var toggleResults = await toggleIsArrayResp.Content.ReadFromJsonAsync<Response[]>(_fixture.JsonOptions);
        Assert.IsNotNull(toggleResults);
        StringAssert.Contains(toggleResults[0].Status, "while property values exist");

        var propsSource = _fixture.HostFactory.Services.GetRequiredService<IPropertiesPersistentSource>();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            propsSource.UpdateProperty(new PropertyApiModel { Name = secureArrayPropName, Secure = true, IsArray = false }));
        StringAssert.Contains(ex.Message, "while property values exist");

        var emptyPropName = "EmptyProp_" + Guid.NewGuid().ToString("N")[..8];
        propsSource.CreateProperty(new PropertyApiModel { Name = emptyPropName, Secure = false, IsArray = false }, "admin.user@sefe.eu");
        Assert.IsTrue(propsSource.UpdateProperty(new PropertyApiModel { Name = emptyPropName, Secure = false, IsArray = true }));

        var scopedResp = await adminClient.PutAsJsonAsync(
            $"/RefDataScopedPropertyValues?scope={childName}&page=1&limit=50",
            new PagedDataOperators(),
            _fixture.JsonOptions);
        Assert.AreEqual(HttpStatusCode.OK, scopedResp.StatusCode);
        var scopedDto = await scopedResp.Content.ReadFromJsonAsync<GetScopedPropertyValuesResponseDto>(_fixture.JsonOptions);
        Assert.IsNotNull(scopedDto);
        var maskedProp = scopedDto.Items.Single(i => i.Property == secureArrayPropName);
        Assert.IsTrue(maskedProp.Secure);
        Assert.AreEqual(string.Empty, maskedProp.PropertyValue);
    }
}
