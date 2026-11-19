using Dorc.ApiModel;
using Dorc.McpServer.Tools;
using NSubstitute;

namespace Dorc.McpServer.Tests;

[TestClass]
public sealed class InfrastructureTopologyToolsTests
{
    [TestMethod]
    public async Task InfrastructureTopologyTools_CoversAllMethodsAndBranches()
    {
        var gateway = Substitute.For<IDorcApiGateway>();
        var allowGuard = new ProductionEnvironmentGuard(new DorcMcpOptions { ReadOnly = false, AllowProd = true });
        var nonProdGuard = new ProductionEnvironmentGuard(new DorcMcpOptions { ReadOnly = false, AllowProd = false });
        var roGuard = new ProductionEnvironmentGuard(new DorcMcpOptions { ReadOnly = true, AllowProd = false });

        Assert.ThrowsExactly<ArgumentNullException>(() => new InfrastructureTopologyTools(null!, allowGuard));
        Assert.ThrowsExactly<ArgumentNullException>(() => new InfrastructureTopologyTools(gateway, null!));

        var tools = new InfrastructureTopologyTools(gateway, allowGuard);
        var nonProdTools = new InfrastructureTopologyTools(gateway, nonProdGuard);
        var roTools = new InfrastructureTopologyTools(gateway, roGuard);

        gateway.GetAsync<AccessSecureApiModel>(
                "AccessControl?accessControlType=Project&accessControlName=Endur",
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new AccessSecureApiModel()));
        Assert.IsTrue((await tools.GetAccessControlAsync(AccessControlType.Project, " Endur ")).Succeeded);

        gateway.GetAsync<List<UserElementApiModel>>(
                "AccessControl/SearchUsers?search=alice",
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<UserElementApiModel>()));
        Assert.IsTrue((await tools.SearchAccessControlIdentitiesAsync(" alice ")).Succeeded);

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => tools.UpdateAccessControlAsync(null!));
        Assert.IsFalse((await roTools.UpdateAccessControlAsync(new AccessSecureApiModel())).Succeeded);
        Assert.IsFalse((await nonProdTools.UpdateAccessControlAsync(new AccessSecureApiModel())).Succeeded);

        gateway.PutAsync<AccessSecureApiModel, AccessSecureApiModel>(
                "AccessControl",
                Arg.Any<AccessSecureApiModel>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new AccessSecureApiModel()));
        Assert.IsTrue((await tools.UpdateAccessControlAsync(new AccessSecureApiModel())).Succeeded);

        gateway.GetAsync<List<PermissionDto>>("RefDataUserPermissions", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<PermissionDto>()));
        Assert.IsTrue((await tools.ListUserPermissionsAsync()).Succeeded);

        gateway.GetAsync<ServerApiModel>("RefDataServers/APP01", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new ServerApiModel()));
        Assert.IsTrue((await tools.GetServerAsync(" APP01 ")).Succeeded);

        gateway.PutAsync<PagedDataOperators, GetServerApiModelListResponseDto>(
                "RefDataServers/ByPage?page=1&limit=25",
                Arg.Any<PagedDataOperators>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetServerApiModelListResponseDto()));
        Assert.IsTrue((await tools.SearchServersAsync(1, 25, null, null, null)).Succeeded);
        Assert.IsTrue((await tools.SearchServersAsync(1, 25, " APP ", " Win ", " Endur ")).Succeeded);

        gateway.GetAsync<List<DatabaseApiModel>>("RefDataDatabases", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<DatabaseApiModel>()));
        Assert.IsTrue((await tools.ListDatabasesAsync(null, null)).Succeeded);

        gateway.GetAsync<List<DatabaseApiModel>>("RefDataDatabases?name=DB1&type=SQL", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<DatabaseApiModel>()));
        Assert.IsTrue((await tools.ListDatabasesAsync(" DB1 ", " SQL ")).Succeeded);

        gateway.PutAsync<PagedDataOperators, GetDatabaseApiModelListResponseDto>(
                "RefDataDatabases/ByPage?page=1&limit=25",
                Arg.Any<PagedDataOperators>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetDatabaseApiModelListResponseDto()));
        Assert.IsTrue((await tools.SearchDatabasesAsync(1, 25, null, null, null)).Succeeded);
        Assert.IsTrue((await tools.SearchDatabasesAsync(1, 25, " DB1 ", " SRV1 ", " SQL ")).Succeeded);

        gateway.GetAsync<List<DaemonApiModel>>("RefDataDaemons", Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new List<DaemonApiModel>()));
        Assert.IsTrue((await tools.ListDaemonsAsync()).Succeeded);
    }
}
