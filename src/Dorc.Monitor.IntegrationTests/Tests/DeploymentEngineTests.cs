using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Collections.Concurrent;

namespace Dorc.Monitor.IntegrationTests.Tests
{
    [TestClass]
    public class DeploymentEngineTests
    {
        [TestMethod]
        public async Task ProcessShouldStopOnUnhandledException()
        {
            var loggerMock = Substitute.For<ILogger<DeploymentEngine>>();
            var drsp = Substitute.For<IDeploymentRequestStateProcessor>();
            var configMock = Substitute.For<IMonitorConfiguration>();
            configMock.MaxConcurrentDeployments.Returns(0); // 0 = unlimited
            drsp.When(d => d.AbandonRequests(Arg.Any<bool>(), Arg.Any<ConcurrentDictionary<int, CancellationTokenSource>>(), Arg.Any<CancellationToken>())).Do(c => throw new ArgumentException());
            var deploymentEngine = new DeploymentEngine(loggerMock, drsp, configMock);

            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await deploymentEngine.ProcessDeploymentRequestsAsync(false, new ConcurrentDictionary<int, CancellationTokenSource>(), new CancellationToken(), 100);
            });
        }

        [TestMethod]
        public async Task ProcessShouldStopOnTokenCancellation()
        {
            var loggerMock = Substitute.For<ILogger<DeploymentEngine>>();
            var iterationDelayMs = 50;
            var drsp = Substitute.For<IDeploymentRequestStateProcessor>();
            var configMock = Substitute.For<IMonitorConfiguration>();
            configMock.MaxConcurrentDeployments.Returns(0); // 0 = unlimited
            CancellationTokenSource source = new CancellationTokenSource();
            var deploymentEngine = new DeploymentEngine(loggerMock, drsp, configMock);
            var task = deploymentEngine.ProcessDeploymentRequestsAsync(false, new ConcurrentDictionary<int, CancellationTokenSource>(), source.Token, iterationDelayMs);
            await Task.Delay(iterationDelayMs * 2);

            source.Cancel();
            var winnerTask = await Task.WhenAny(task, Task.Delay(iterationDelayMs * 2));

            if (winnerTask != task)
                Assert.Fail("Timeout finished earlier than process service task");
        }
    }
}
