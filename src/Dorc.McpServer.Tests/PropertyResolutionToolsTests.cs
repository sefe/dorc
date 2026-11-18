using Dorc.ApiModel;
using Dorc.McpServer.Tools;
using NSubstitute;

namespace Dorc.McpServer.Tests;

[TestClass]
public sealed class PropertyResolutionToolsTests
{
    [TestMethod]
    public async Task PropertyResolutionTools_CoversAllMethodsAndBranches()
    {
        var gateway = Substitute.For<IDorcApiGateway>();
        Assert.ThrowsExactly<ArgumentNullException>(() => new PropertyResolutionTools(null!));

        var tools = new PropertyResolutionTools(gateway);

        // GetPropertyValuesAsync: no filters (validation error), both filters, failure
        var noFiltersResult = await tools.GetPropertyValuesAsync(null, null);
        Assert.IsFalse(noFiltersResult.Succeeded);
        StringAssert.Contains(noFiltersResult.Error!, "At least one of 'propertyName' or 'environmentName' must be provided");

        gateway.GetAsync<List<PropertyValueDto>>("PropertyValues?propertyName=P1&environmentName=DEV", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<PropertyValueDto>()));
        Assert.IsTrue((await tools.GetPropertyValuesAsync(" P1 ", " DEV ")).Succeeded);

        gateway.GetAsync<List<PropertyValueDto>>("PropertyValues?environmentName=DEV", Arg.Any<CancellationToken>())
            .Returns(new McpToolOutcome<List<PropertyValueDto>>(true, new List<PropertyValueDto>(), null, null));
        Assert.IsTrue((await tools.GetPropertyValuesAsync(null, "DEV")).Succeeded);

        gateway.GetAsync<List<PropertyValueDto>>("PropertyValues?propertyName=ERR", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<List<PropertyValueDto>>("fail", 500));
        Assert.IsFalse((await tools.GetPropertyValuesAsync("ERR", null)).Succeeded);

        // SearchPropertyValuesAsync: with and without filters, failure
        gateway.PutAsync<PagedDataOperators, GetScopedPropertyValuesResponseDto>(
                "RefDataSearchPropertyValues?page=1&limit=25",
                Arg.Any<PagedDataOperators>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetScopedPropertyValuesResponseDto { Items = [] }));
        Assert.IsTrue((await tools.SearchPropertyValuesAsync(1, 25, "Prop", "Scope")).Succeeded);
        Assert.IsTrue((await tools.SearchPropertyValuesAsync(1, 25, null, null)).Succeeded);

        gateway.PutAsync<PagedDataOperators, GetScopedPropertyValuesResponseDto>(
                "RefDataSearchPropertyValues?page=2&limit=10",
                Arg.Any<PagedDataOperators>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<GetScopedPropertyValuesResponseDto>("fail", 500));
        Assert.IsFalse((await tools.SearchPropertyValuesAsync(2, 10)).Succeeded);

        // GetPropertyScopeOptionsAsync: success and failure
        gateway.GetAsync<List<PropertyValueScopeOptionApiModel>>("PropertyValues/ScopeOptions?propertyValueScope=DEV", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<PropertyValueScopeOptionApiModel>()));
        Assert.IsTrue((await tools.GetPropertyScopeOptionsAsync("DEV")).Succeeded);

        gateway.GetAsync<List<PropertyValueScopeOptionApiModel>>("PropertyValues/ScopeOptions?propertyValueScope=ERR", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<List<PropertyValueScopeOptionApiModel>>("fail", 500));
        Assert.IsFalse((await tools.GetPropertyScopeOptionsAsync("ERR")).Succeeded);

        // SearchPropertyValuesAuditAsync: with filters, without filters, failure
        gateway.PutAsync<PagedDataOperators, GetPropertyValuesAuditListResponseDto>(
                "PropertyValuesAudit?page=1&limit=25&useAndLogic=true",
                Arg.Any<PagedDataOperators>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetPropertyValuesAuditListResponseDto { Items = [] }));
        Assert.IsTrue((await tools.SearchPropertyValuesAuditAsync(1, 25, "P", "E", "U")).Succeeded);
        Assert.IsTrue((await tools.SearchPropertyValuesAuditAsync(1, 25, null, null, null)).Succeeded);

        gateway.PutAsync<PagedDataOperators, GetPropertyValuesAuditListResponseDto>(
                "PropertyValuesAudit?page=2&limit=10&useAndLogic=true",
                Arg.Any<PagedDataOperators>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Failure<GetPropertyValuesAuditListResponseDto>("fail", 500));
        Assert.IsFalse((await tools.SearchPropertyValuesAuditAsync(2, 10)).Succeeded);
    }
}
