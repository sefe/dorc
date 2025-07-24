using log4net;
using NSubstitute;
using System.Collections.Concurrent;

namespace Dorc.Monitor.IntegrationTests.Tests
{
    [TestClass]
    public class DeploymentEngineTests
    {
        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public async Task ProcessShouldStopOnUnhandledException()
        {
            var loggerMock = Substitute.For<ILog>();
            var drsp = Substitute.For<IDeploymentRequestStateProcessor>();
            var serviceProviderMock = Substitute.For<IServiceProvider>();
            drsp.When(d => d.AbandonRequests(Arg.Any<bool>(), Arg.Any<ConcurrentDictionary<int, CancellationTokenSource>>(), Arg.Any<CancellationToken>())).Do(c => throw new ArgumentException());
            var deploymentEngine = new DeploymentEngine(loggerMock, drsp, serviceProviderMock);
            await deploymentEngine.ProcessDeploymentRequestsAsync(false, new ConcurrentDictionary<int, CancellationTokenSource>(), new CancellationToken(), 100);

            Assert.Fail("method should throw exception and never get here");
        }

        [TestMethod]
        public async Task ProcessShouldStopOnTokenCancellation()
        {
            var loggerMock = Substitute.For<ILog>();
            var iterationDelayMs = 50;
            var drsp = Substitute.For<IDeploymentRequestStateProcessor>();
            var serviceProviderMock = Substitute.For<IServiceProvider>();
            CancellationTokenSource source = new CancellationTokenSource();
            var deploymentEngine = new DeploymentEngine(loggerMock, drsp, serviceProviderMock);
            var task = deploymentEngine.ProcessDeploymentRequestsAsync(false, new ConcurrentDictionary<int, CancellationTokenSource>(), source.Token, iterationDelayMs);
            await Task.Delay(iterationDelayMs * 2);

            source.Cancel();
            var winnerTask = await Task.WhenAny(task, Task.Delay(iterationDelayMs * 2));

            if (winnerTask != task)
                Assert.Fail("Timeout finished earlier than process service task");
        }
    }
}
