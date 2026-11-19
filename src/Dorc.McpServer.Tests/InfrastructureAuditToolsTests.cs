using Dorc.ApiModel;
using Dorc.McpServer.Tools;
using NSubstitute;

namespace Dorc.McpServer.Tests;

[TestClass]
public sealed class InfrastructureAuditToolsTests
{
    [TestMethod]
    public async Task InfrastructureAuditTools_CoversAllMethodsAndBranches()
    {
        var gateway = Substitute.For<IDorcApiGateway>();
        Assert.ThrowsExactly<ArgumentNullException>(() => new InfrastructureAuditTools(null!));

        var tools = new InfrastructureAuditTools(gateway);

        gateway.PutAsync<PagedDataOperators, GetScriptsAuditListResponseDto>(
                "ScriptsAudit?page=1&limit=25&useAndLogic=true",
                Arg.Any<PagedDataOperators>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetScriptsAuditListResponseDto()));
        Assert.IsTrue((await tools.SearchScriptsAuditAsync(1, 25, null, null, null)).Succeeded);
        Assert.IsTrue((await tools.SearchScriptsAuditAsync(1, 25, " s.ps1 ", " user ", " proj ")).Succeeded);

        gateway.PutAsync<PagedDataOperators, GetRefDataAuditListResponseDto>(
                "RefDataProjectAudit?page=1&limit=25",
                Arg.Any<PagedDataOperators>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetRefDataAuditListResponseDto()));
        Assert.IsTrue((await tools.SearchProjectAuditAsync(1, 25, null, null, null)).Succeeded);

        gateway.PutAsync<PagedDataOperators, GetRefDataAuditListResponseDto>(
                "RefDataProjectAudit?projectId=7&page=1&limit=25",
                Arg.Any<PagedDataOperators>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetRefDataAuditListResponseDto()));
        Assert.IsTrue((await tools.SearchProjectAuditAsync(1, 25, 7, " u ", " Update ")).Succeeded);

        gateway.GetAsync<GetDaemonObservationListResponseDto>(
                "DaemonObservation?daemonId=3&page=1&limit=50",
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetDaemonObservationListResponseDto()));
        Assert.IsTrue((await tools.GetDaemonObservationsAsync(3, null, 1, 50)).Succeeded);

        gateway.GetAsync<GetDaemonObservationListResponseDto>(
                "DaemonObservation?daemonId=3&serverId=9&page=1&limit=50",
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetDaemonObservationListResponseDto()));
        Assert.IsTrue((await tools.GetDaemonObservationsAsync(3, 9, 1, 50)).Succeeded);

        gateway.PutAsync<PagedDataOperators, GetDaemonAuditListResponseDto>(
                "DaemonAudit?page=1&limit=25",
                Arg.Any<PagedDataOperators>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetDaemonAuditListResponseDto()));
        Assert.IsTrue((await tools.SearchDaemonAuditAsync(1, 25, null, null, null)).Succeeded);

        gateway.PutAsync<PagedDataOperators, GetDaemonAuditListResponseDto>(
                "DaemonAudit?daemonId=4&page=1&limit=25",
                Arg.Any<PagedDataOperators>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetDaemonAuditListResponseDto()));
        Assert.IsTrue((await tools.SearchDaemonAuditAsync(1, 25, 4, " svc ", " u ")).Succeeded);

        gateway.PutAsync<PagedDataOperators, GetServerAuditListResponseDto>(
                "ServerAudit?page=1&limit=25",
                Arg.Any<PagedDataOperators>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetServerAuditListResponseDto()));
        Assert.IsTrue((await tools.SearchServerAuditAsync(1, 25, null, null)).Succeeded);
        Assert.IsTrue((await tools.SearchServerAuditAsync(1, 25, " srv ", " u ")).Succeeded);

        gateway.PutAsync<PagedDataOperators, GetDatabaseAuditListResponseDto>(
                "DatabaseAudit?page=1&limit=25",
                Arg.Any<PagedDataOperators>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetDatabaseAuditListResponseDto()));
        Assert.IsTrue((await tools.SearchDatabaseAuditAsync(1, 25, null, null)).Succeeded);
        Assert.IsTrue((await tools.SearchDatabaseAuditAsync(1, 25, " db ", " u ")).Succeeded);

        gateway.PutAsync<PagedDataOperators, GetComponentAuditListResponseDto>(
                "ContainerAudit?page=1&limit=25",
                Arg.Any<PagedDataOperators>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetComponentAuditListResponseDto()));
        Assert.IsTrue((await tools.SearchContainerAuditAsync(1, 25, null, null, null)).Succeeded);

        gateway.PutAsync<PagedDataOperators, GetComponentAuditListResponseDto>(
                "ContainerAudit?containerId=11&page=1&limit=25",
                Arg.Any<PagedDataOperators>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetComponentAuditListResponseDto()));
        Assert.IsTrue((await tools.SearchContainerAuditAsync(1, 25, 11, " comp ", " u ")).Succeeded);

        gateway.PutAsync<PagedDataOperators, GetComponentAuditListResponseDto>(
                "CloudResourceAudit?page=1&limit=25",
                Arg.Any<PagedDataOperators>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetComponentAuditListResponseDto()));
        Assert.IsTrue((await tools.SearchCloudResourceAuditAsync(1, 25, null, null, null)).Succeeded);

        gateway.PutAsync<PagedDataOperators, GetComponentAuditListResponseDto>(
                "CloudResourceAudit?cloudResourceId=15&page=1&limit=25",
                Arg.Any<PagedDataOperators>(),
                Arg.Any<CancellationToken>())
            .Returns(McpToolOutcome.Success(new GetComponentAuditListResponseDto()));
        Assert.IsTrue((await tools.SearchCloudResourceAuditAsync(1, 25, 15, " res ", " u ")).Succeeded);
    }
}
