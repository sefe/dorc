using Dorc.ApiModel;
using Dorc.Monitor.RequestProcessors;
using Dorc.Monitor.Tests.Data;
using Dorc.Monitor.Tests.Init;
using JasperFx.Core;
using NSubstitute;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;

namespace Dorc.Monitor.IntegrationTests.Tests
{
    /// <summary>
    /// this test class is using real database configured in appsettings.test.json
    /// </summary>
    [TestClass]
    public class MonitorServiceTests : MonitorServiceTestBase
    {
        [TestMethod]
        public async Task ProcessDeploymentRequestsInParallelForDifferentEnvironments()
        {
            // Arrange
            var envs = getEnvs();
            // here we are using usernames as call sequence identifiers, project as request duration.
            // Task switch + DB call for requests takes about 70ms, so to guarantee sequence need difference about 100 ms between tasks
            var data = new[]
            {
                new { Env = envs[0], Username = "1", Project = "2000" }, // while this is running all others in other envs should finish
                new { Env = envs[0], Username = "2", Project = "100" },
                new { Env = envs[0], Username = "3", Project = "100" },
                new { Env = envs[0], Username = "4", Project = "100" },
                new { Env = envs[1], Username = "1", Project = "700" },
                new { Env = envs[1], Username = "a", Project = "100" },
                new { Env = envs[2], Username = "1", Project = "300" },
                new { Env = envs[2], Username = "b", Project = "500" },
            };

            var requests = data.Map(d => DeploymentRequestData.GetDeploymentRequest(d.Env, d.Username, d.Project));

            AddDeploymentRequests(requests);

            var queue = new ConcurrentQueue<string>();
            var dict = new ConcurrentDictionary<string, string>();
            var stopWatch = new Stopwatch();

            var pendingRequestProcessorMock = SetupPendingRequestProcessorMock(envs, queue, dict, stopWatch);

            var threadsCount = ThreadPool.ThreadCount;
            ThreadPool.SetMinThreads(32, 8);
            int min, io, available;
            ThreadPool.GetMinThreads(out min, out io);
            ThreadPool.GetAvailableThreads(out available, out io);
            Console.WriteLine($"threads in pool: {threadsCount} min:{min} available:{available}");

            // Act
            stopWatch.Start();
            var monitorService = GetMonitorService();
            CancellationTokenSource source = new CancellationTokenSource();
            CancellationToken token = source.Token;
            stopWatch.Start();
            await monitorService.StartAsync(token);

            await Task.Delay(100); // give time for service to start

            // for debugging: uncomment next 2 lines 
            //if (monitorService.ExecuteTask != null)
            //    await monitorService.ExecuteTask; // wait until background task is finished

            // for debugging: comment next 3 lines
            await Task.Delay(15000); // wait some time and stop service and check results
            source.Cancel();
            await monitorService.StopAsync(token);

            stopWatch.Stop();
            DeleteAllAddedDeploymentRequests();

            // Assert
            // tasks start sequence 
            pendingRequestProcessorMock.Received(requests.Count());
            string sequence = string.Join("", queue);
            // requests for every environment should start one after another
            Assert.AreEqual("1234", dict[envs[0]], true, $"{envs[0]} sequence is bad");
            Assert.AreEqual("1a", dict[envs[1]], true, $"{envs[1]} sequence is bad");
            Assert.AreEqual("1b", dict[envs[2]], true, $"{envs[2]} sequence is bad");
        }

        [TestMethod]
        public async Task CancelRequestsShouldCancelWhenExecuteIsRunning()
        {
            // Arrange
            string[] envs = getEnvs();
            // here we are using usernames as call sequence identifiers, project as request duration
            var data = new[]
            {
                new { Env = envs[0], Username = "q", Project = "1000" },
                new { Env = envs[0], Username = "w", Project = "10000" },
                new { Env = envs[0], Username = "e", Project = "100" },
            };

            var requests = data.Map(d =>
                DeploymentRequestData.GetDeploymentRequest(d.Env, d.Username, d.Project));
            var requestToCancel = requests[1];
            AddDeploymentRequests(requests);

            var queue = new ConcurrentQueue<string>();
            var dict = new ConcurrentDictionary<string, string>();
            var stopWatch = new Stopwatch();

            var pendingRequestProcessorMock = SetupPendingRequestProcessorMock(envs, queue, dict, stopWatch);
            SubstituteTransientWith(Substitute.For<IComponentProcessor>());

            // Act
            stopWatch.Start();
            var monitorService = GetMonitorService();
            CancellationTokenSource source = new CancellationTokenSource();
            CancellationToken token = source.Token;
            stopWatch.Start();

            await monitorService.StartAsync(token);

            await Task.Delay(100); // give time for service to start processing

            UpdateDeploymentRequestStatus(requestToCancel, DeploymentRequestStatus.Cancelling);

            await Task.Delay(3000); // give time for service to cancel request

            source.Cancel();
            await monitorService.StopAsync(token);

            stopWatch.Stop();

            // Assert
            pendingRequestProcessorMock.Received(2);
            string sequence = string.Join("", queue);
            Assert.AreEqual("qe", sequence);

            requestToCancel = GetUpdatedDeploymentRequest(requestToCancel);
            Assert.AreEqual(DeploymentRequestStatus.Cancelled.ToString(), requestToCancel.Status);

            DeleteAllAddedDeploymentRequests();
        }

        private static string[] getEnvs()
        {
            var ext = RandomNumberGenerator.GetHexString(3);
            var envs = new string[] { "env1_" + ext, "env2_" + ext, "env3_" + ext };
            Console.WriteLine($"current ext: {ext}");
            return envs;
        }

        private IPendingRequestProcessor SetupPendingRequestProcessorMock(IList<string> envsToCheck, ConcurrentQueue<string> queue, ConcurrentDictionary<string, string> dict, Stopwatch stopWatch)
        {
            var prProcessorMock = Substitute.For<IPendingRequestProcessor>();
            prProcessorMock.When(s => s.ExecuteAsync(Arg.Any<RequestToProcessDto>(), Arg.Any<CancellationToken>()))
                .Do(c =>
                {
                    var a = c.Arg<RequestToProcessDto>();
                    var b = c.Arg<CancellationToken>();
                    if (envsToCheck.Contains(a.Request.EnvironmentName))
                    {
                        var sleepTime = int.Parse(a.Request.Project);
                        Console.WriteLine($"{stopWatch.Elapsed}   start {a.Request.UserName} {a.Request.EnvironmentName} duration:{sleepTime}");

                        queue.Enqueue(a.Request.UserName);
                        dict.AddOrUpdate(a.Request.EnvironmentName, a.Request.UserName, (key, oldValue) => oldValue += a.Request.UserName);

                        Task.Delay(sleepTime);
                        Console.WriteLine($"{stopWatch.Elapsed}   stop {a.Request.UserName} {a.Request.EnvironmentName}");
                    }
                    else
                    {
                        Console.WriteLine($"wrong environment found: {a.Request.EnvironmentName}, test results are not valid, remove all DeploymentRequests from DB and start test again");
                    }
                });

            SubstituteTransientWith(prProcessorMock);
            return prProcessorMock;
        }
    }
}