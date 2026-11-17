using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dorc.ApiModel;
using Dorc.Core;
using Microsoft.Extensions.Configuration;
using RestSharp;
using Tools.RequestCLI;

namespace Tools.RequestCLI.Tests
{
    [TestClass]
    public class EphemeralCliTests
    {
        [TestMethod]
        public void IsEphemeralCommand_DetectsEphemeralPrefixCaseInsensitively()
        {
            Assert.IsFalse(EphemeralCliArgs.IsEphemeralCommand(null));
            Assert.IsFalse(EphemeralCliArgs.IsEphemeralCommand(Array.Empty<string>()));
            Assert.IsFalse(EphemeralCliArgs.IsEphemeralCommand(new[] { "/project:MyProj" }));
            Assert.IsTrue(EphemeralCliArgs.IsEphemeralCommand(new[] { "ephemeral", "up" }));
            Assert.IsTrue(EphemeralCliArgs.IsEphemeralCommand(new[] { "EPHEMERAL", "DOWN" }));
        }

        [TestMethod]
        public void EphemeralCliArgs_UpAndDownParsingAndValidation_CoversAllBranches()
        {
            // Invalid command prefixes / verbs
            Assert.ThrowsExactly<ArgumentException>(() => new EphemeralCliArgs(null!));
            Assert.ThrowsExactly<ArgumentException>(() => new EphemeralCliArgs(new[] { "ephemeral" }));
            Assert.ThrowsExactly<ArgumentException>(() => new EphemeralCliArgs(new[] { "deploy", "up" }));
            Assert.ThrowsExactly<ArgumentException>(() => new EphemeralCliArgs(new[] { "ephemeral", "restart" }));

            // Up with --key value, --key=value, /key:value, flags
            var upArgs = new EphemeralCliArgs(new[]
            {
                "ephemeral", "up",
                "--project", "Traveler",
                "--template=Full-Stack",
                "/targetenv:eph-pr-42",
                "--tier", "ephemeral",
                "--ttl-minutes", "180",
                "--timeout-minutes", "45",
                "--build-num", "20260417.9",
                "--build-url", "https://dev.azure.com/org/proj/_build/results?buildId=9",
                "--base-env", "Traveler DV 01",
                "--property", "sku_name=B_Standard_B1ms;storage_mb=32768",
                "--reason", "PR 42 test",
                "--no-wait",
                "--wait",
                "--block",
                "--force-delete",
                "--run-validation",
                "--block", "false"
            });

            Assert.AreEqual(EphemeralCliVerb.Up, upArgs.Verb);
            Assert.AreEqual("Traveler", upArgs.Project);
            Assert.AreEqual("Full-Stack", upArgs.Template);
            Assert.AreEqual("eph-pr-42", upArgs.TargetEnv);
            Assert.AreEqual("ephemeral", upArgs.Tier);
            Assert.AreEqual(180, upArgs.TtlMinutes);
            Assert.AreEqual(45, upArgs.TimeoutMinutes);
            Assert.AreEqual("20260417.9", upArgs.BuildNum);
            Assert.AreEqual("https://dev.azure.com/org/proj/_build/results?buildId=9", upArgs.BuildUrl);
            Assert.AreEqual("Traveler DV 01", upArgs.BaseEnv);
            Assert.AreEqual("B_Standard_B1ms", upArgs.Properties["sku_name"]);
            Assert.AreEqual("32768", upArgs.Properties["storage_mb"]);
            Assert.AreEqual("PR 42 test", upArgs.Reason);
            Assert.IsFalse(upArgs.Block);
            Assert.IsTrue(upArgs.ForceDeleteMetadataOnNoComponents);
            Assert.IsTrue(upArgs.RunValidationComponents);

            var trailingBlockArgs = new EphemeralCliArgs(new[]
            {
                "ephemeral", "down",
                "--target-env", "eph-pr-42",
                "/block:false",
                "--block"
            });
            Assert.IsTrue(trailingBlockArgs.Block);

            // Down with aliases
            var downArgs = new EphemeralCliArgs(new[]
            {
                "ephemeral", "down",
                "--target-env", "eph-pr-41",
                "--env", "eph-pr-42",
                "--ttlminutes", "30",
                "/timeout:15",
                "/buildnum:1.2.3",
                "/buildurl:https://build/123",
                "--baseenv", "Base1",
                "--properties", "k=v",
                "/reason:Done testing",
                "/wait:true"
            });
            Assert.AreEqual(EphemeralCliVerb.Down, downArgs.Verb);
            Assert.AreEqual("eph-pr-42", downArgs.TargetEnv);
            Assert.AreEqual(30, downArgs.TtlMinutes);
            Assert.AreEqual(15, downArgs.TimeoutMinutes);
            Assert.AreEqual("1.2.3", downArgs.BuildNum);
            Assert.AreEqual("https://build/123", downArgs.BuildUrl);
            Assert.AreEqual("Base1", downArgs.BaseEnv);
            Assert.AreEqual("v", downArgs.Properties["k"]);
            Assert.AreEqual("Done testing", downArgs.Reason);
            Assert.IsTrue(downArgs.Block);

            // Error cases
            Assert.ThrowsExactly<ArgumentException>(() => new EphemeralCliArgs(new[] { "ephemeral", "up", "/:bad" }));
            Assert.ThrowsExactly<ArgumentException>(() => new EphemeralCliArgs(new[] { "ephemeral", "up", "--project" }));
            Assert.ThrowsExactly<ArgumentException>(() => new EphemeralCliArgs(new[] { "ephemeral", "up", "positional" }));
            Assert.ThrowsExactly<ArgumentException>(() => new EphemeralCliArgs(new[] { "ephemeral", "up", "--project", "P", "--target-env", "E", "--ttl", "0" }));
            Assert.ThrowsExactly<ArgumentException>(() => new EphemeralCliArgs(new[] { "ephemeral", "up", "--project", "P", "--target-env", "E", "--ttl", "abc" }));
            Assert.ThrowsExactly<ArgumentException>(() => new EphemeralCliArgs(new[] { "ephemeral", "up", "--project", "P", "--target-env", "E", "--timeout", "0" }));
            Assert.ThrowsExactly<ArgumentException>(() => new EphemeralCliArgs(new[] { "ephemeral", "up", "--project", "P", "--target-env", "E", "--timeout-minutes", "abc" }));
            Assert.ThrowsExactly<ArgumentException>(() => new EphemeralCliArgs(new[] { "ephemeral", "up", "--project", "P", "--target-env", "E", "/wait:not-a-bool" }));
            Assert.ThrowsExactly<ArgumentException>(() => new EphemeralCliArgs(new[] { "ephemeral", "up", "--project", "P", "--target-env", "E", "--unknown", "1" }));
            Assert.ThrowsExactly<ArgumentException>(() => new EphemeralCliArgs(new[] { "ephemeral", "up", "--project", "P", "--target-env", "E", "--prop", "badprop" }));
            Assert.ThrowsExactly<ArgumentException>(() => new EphemeralCliArgs(new[] { "ephemeral", "up", "--project", "P", "--target-env", "E", "--prop", "key=" }));
            Assert.ThrowsExactly<ArgumentException>(() => new EphemeralCliArgs(new[] { "ephemeral", "down" }));
            Assert.ThrowsExactly<ArgumentException>(() => new EphemeralCliArgs(new[] { "ephemeral", "up", "--target-env", "E" }));
        }

        [TestMethod]
        public async Task EphemeralEnvironmentCliClient_ProvisionAndTeardown_CoversAllBranches()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => new EphemeralEnvironmentCliClient(null!));

            var defaultClient = new EphemeralEnvironmentCliClient(new HttpClient { BaseAddress = new Uri("https://localhost/") });
            await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => defaultClient.ExecuteAsync(null!));

            var logs = new List<string>();
            var requestStatusSequence = new Queue<RequestStatusDto?>(new[]
            {
                new RequestStatusDto { Id = 101, Status = "Running" },
                new RequestStatusDto { Id = 101, Status = "Running" },
                new RequestStatusDto { Id = 101, Status = "Completed" },
                new RequestStatusDto { Id = 102, Status = "Completed" }
            });

            var handler = new StubHttpMessageHandler(req =>
            {
                var path = req.RequestUri!.PathAndQuery;
                if (path == "/RefDataProjects/Traveler")
                {
                    return JsonResponse(new ProjectApiModel { ProjectId = 10, ProjectName = "Traveler" });
                }
                if (path == "/EphemeralEnvironments/templates?projectId=10")
                {
                    return JsonResponse(new List<EnvironmentTemplateApiModel>
                    {
                        new() { Id = 5, ProjectId = 10, Name = "Full-Stack" }
                    });
                }
                if (path == "/RefDataEnvironments?env=Traveler%20DV%2001")
                {
                    return JsonResponse(new List<EnvironmentApiModel>
                    {
                        new() { EnvironmentId = 20, EnvironmentName = "Traveler DV 01" }
                    });
                }
                if (path == "/EphemeralEnvironments/provision")
                {
                    return JsonResponse(new EphemeralEnvironmentProvisionResponseApiModel
                    {
                        Environment = new EnvironmentApiModel { EnvironmentId = 500, EnvironmentName = "eph-pr-1" },
                        ProvisionRequestIds = new List<int> { 101 },
                        ValidationRequestIds = new List<int> { 102 }
                    });
                }
                if (path.StartsWith("/Request?id=", StringComparison.Ordinal))
                {
                    var next = requestStatusSequence.Dequeue();
                    return JsonResponse(next);
                }
                if (path == "/EphemeralEnvironments/eph-pr-1/teardown")
                {
                    return JsonResponse(new EphemeralEnvironmentTeardownResponseApiModel
                    {
                        EnvironmentName = "eph-pr-1",
                        EphemeralState = EphemeralEnvironmentStates.TearingDown,
                        DestroyRequestId = 201
                    });
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
            var sut = new EphemeralEnvironmentCliClient(httpClient, logs.Add, TimeSpan.FromMilliseconds(1));

            // 1) Up with name resolution and blocking wait
            var upResult = await sut.ExecuteAsync(new EphemeralCliArgs(new[]
            {
                "ephemeral", "up",
                "--project", "Traveler",
                "--template", "Full-Stack",
                "--target-env", "eph-pr-1",
                "--base-env", "Traveler DV 01"
            }));
            Assert.AreEqual(0, upResult);

            // 2) Up with numeric IDs, no template/base-env, and --no-wait (plus null Environment in response)
            var noWaitHandler = new StubHttpMessageHandler(_ => JsonResponse(new EphemeralEnvironmentProvisionResponseApiModel
            {
                Environment = null!
            }));
            using var noWaitHttp = new HttpClient(noWaitHandler) { BaseAddress = new Uri("https://localhost/") };
            var noWaitClient = new EphemeralEnvironmentCliClient(noWaitHttp, logs.Add, TimeSpan.FromMilliseconds(1));
            var noWaitResult = await noWaitClient.ExecuteAsync(new EphemeralCliArgs(new[]
            {
                "ephemeral", "up",
                "--project", "10",
                "--target-env", "eph-pr-2",
                "--no-wait"
            }));
            Assert.AreEqual(0, noWaitResult);

            // 3) Up with numeric templateId and baseEnvId, and request poll returning Failed
            var failedPollHandler = new StubHttpMessageHandler(req =>
            {
                if (req.RequestUri!.PathAndQuery == "/EphemeralEnvironments/provision")
                {
                    return JsonResponse(new EphemeralEnvironmentProvisionResponseApiModel
                    {
                        Environment = new EnvironmentApiModel { EnvironmentName = "eph-pr-3" },
                        ProvisionRequestIds = new List<int> { 301 }
                    });
                }
                return JsonResponse(new RequestStatusDto { Id = 301, Status = "Failed" });
            });
            using var failedPollHttp = new HttpClient(failedPollHandler) { BaseAddress = new Uri("https://localhost/") };
            var failedPollClient = new EphemeralEnvironmentCliClient(failedPollHttp, null, TimeSpan.FromMilliseconds(1));
            var failedPollResult = await failedPollClient.ExecuteAsync(new EphemeralCliArgs(new[]
            {
                "ephemeral", "up",
                "--project", "10",
                "--template", "5",
                "--base-env", "20",
                "--target-env", "eph-pr-3"
            }));
            Assert.AreEqual(2, failedPollResult);

            // 3b) Poll returning Abandoned, WaitingConfirmation, or exceeding TimeoutMinutes deadline returns 2
            foreach (var terminalFailureStatus in new[] { "Abandoned", "WaitingConfirmation" })
            {
                using var termHttp = new HttpClient(new StubHttpMessageHandler(req =>
                {
                    if (req.RequestUri!.PathAndQuery == "/EphemeralEnvironments/provision")
                    {
                        return JsonResponse(new EphemeralEnvironmentProvisionResponseApiModel
                        {
                            Environment = new EnvironmentApiModel { EnvironmentName = "eph-term" },
                            ProvisionRequestIds = new List<int> { 302 }
                        });
                    }
                    return JsonResponse(new RequestStatusDto { Id = 302, Status = terminalFailureStatus });
                })) { BaseAddress = new Uri("https://localhost/") };
                var termClient = new EphemeralEnvironmentCliClient(termHttp, logs.Add, TimeSpan.FromMilliseconds(1));
                Assert.AreEqual(2, await termClient.ExecuteAsync(new EphemeralCliArgs(new[]
                {
                    "ephemeral", "up", "--project", "10", "--target-env", "eph-term"
                })));
            }

            var currentUtc = DateTimeOffset.UtcNow;
            using var timeoutHttp = new HttpClient(new StubHttpMessageHandler(req =>
            {
                if (req.RequestUri!.PathAndQuery == "/EphemeralEnvironments/provision")
                {
                    return JsonResponse(new EphemeralEnvironmentProvisionResponseApiModel
                    {
                        Environment = new EnvironmentApiModel { EnvironmentName = "eph-timeout" },
                        ProvisionRequestIds = new List<int> { 303 }
                    });
                }
                currentUtc = currentUtc.AddMinutes(10);
                return JsonResponse(new RequestStatusDto { Id = 303, Status = "Running" });
            })) { BaseAddress = new Uri("https://localhost/") };
            var timeoutClient = new EphemeralEnvironmentCliClient(timeoutHttp, logs.Add, TimeSpan.FromMilliseconds(1), () => currentUtc);
            Assert.AreEqual(2, await timeoutClient.ExecuteAsync(new EphemeralCliArgs(new[]
            {
                "ephemeral", "up", "--project", "10", "--target-env", "eph-timeout", "--timeout-minutes", "5"
            })));
            var zeroTimeoutArgs = new EphemeralCliArgs(new[] { "ephemeral", "up", "--project", "10", "--target-env", "eph-timeout" })
            {
                TimeoutMinutes = 0
            };
            Assert.AreEqual(2, await timeoutClient.ExecuteAsync(zeroTimeoutArgs));

            // 4) Up with HTTP error, and Up with null JSON body
            using var errorHttp = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("bad request")
            })) { BaseAddress = new Uri("https://localhost/") };
            var errorClient = new EphemeralEnvironmentCliClient(errorHttp);
            Assert.AreEqual(1, await errorClient.ExecuteAsync(new EphemeralCliArgs(new[] { "ephemeral", "up", "--project", "10", "--target-env", "e", "--no-wait" })));
            Assert.AreEqual(1, await errorClient.ExecuteAsync(new EphemeralCliArgs(new[] { "ephemeral", "down", "--target-env", "e", "--no-wait" })));

            using var nullJsonHttp = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("null", Encoding.UTF8, "application/json")
            })) { BaseAddress = new Uri("https://localhost/") };
            var nullJsonClient = new EphemeralEnvironmentCliClient(nullJsonHttp);
            Assert.AreEqual(1, await nullJsonClient.ExecuteAsync(new EphemeralCliArgs(new[] { "ephemeral", "up", "--project", "10", "--target-env", "e", "--no-wait" })));
            Assert.AreEqual(1, await nullJsonClient.ExecuteAsync(new EphemeralCliArgs(new[] { "ephemeral", "down", "--target-env", "e", "--no-wait" })));

            // 5) Down with blocking wait (Completed, empty status returning 2), and Down without DestroyRequestId, and Down with TimeoutMinutes = 0
            requestStatusSequence.Enqueue(new RequestStatusDto { Id = 201, Status = "Completed" });
            var downCompletedResult = await sut.ExecuteAsync(new EphemeralCliArgs(new[] { "ephemeral", "down", "--target-env", "eph-pr-1" }));
            Assert.AreEqual(0, downCompletedResult);

            requestStatusSequence.Enqueue(new RequestStatusDto { Id = 201, Status = "" });
            var downEmptyStatusResult = await sut.ExecuteAsync(new EphemeralCliArgs(new[] { "ephemeral", "down", "--target-env", "eph-pr-1" }));
            Assert.AreEqual(2, downEmptyStatusResult);

            requestStatusSequence.Enqueue(null);
            var downNullStatusResult = await sut.ExecuteAsync(new EphemeralCliArgs(new[] { "ephemeral", "down", "--target-env", "eph-pr-1" }) { TimeoutMinutes = 0 });
            Assert.AreEqual(2, downNullStatusResult);

            // 5b) Up with --run-validation polling GET /EphemeralEnvironments until Ready, or failing with 1 on ValidationFailed/ProvisionFailed/TeardownFailed/TornDown/missing/timeout
            var ephStatesQueue = new Queue<List<EnvironmentApiModel>?>(new[]
            {
                new List<EnvironmentApiModel> { new() { EnvironmentName = "eph-val", EphemeralState = EphemeralEnvironmentStates.Testing } },
                new List<EnvironmentApiModel> { new() { EnvironmentName = "eph-val", EphemeralState = EphemeralEnvironmentStates.Testing } },
                new List<EnvironmentApiModel> { new() { EnvironmentName = "eph-val", EphemeralState = EphemeralEnvironmentStates.Ready } }
            });
            using var valHttp = new HttpClient(new StubHttpMessageHandler(req =>
            {
                var p = req.RequestUri!.PathAndQuery;
                if (p == "/EphemeralEnvironments/provision")
                {
                    return JsonResponse(new EphemeralEnvironmentProvisionResponseApiModel
                    {
                        Environment = new EnvironmentApiModel { EnvironmentName = "eph-val" },
                        ProvisionRequestIds = new List<int> { 701 }
                    });
                }
                if (p == "/Request?id=701")
                {
                    return JsonResponse(new RequestStatusDto { Id = 701, Status = "Completed" });
                }
                if (p == "/EphemeralEnvironments?projectId=10" || p == "/EphemeralEnvironments")
                {
                    return JsonResponse(ephStatesQueue.Dequeue());
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            })) { BaseAddress = new Uri("https://localhost/") };
            var valClient = new EphemeralEnvironmentCliClient(valHttp, logs.Add, TimeSpan.FromMilliseconds(1));
            Assert.AreEqual(0, await valClient.ExecuteAsync(new EphemeralCliArgs(new[]
            {
                "ephemeral", "up", "--project", "10", "--target-env", "eph-val", "--run-validation"
            })));

            foreach (var failedState in new[]
            {
                EphemeralEnvironmentStates.ValidationFailed,
                EphemeralEnvironmentStates.ProvisionFailed,
                EphemeralEnvironmentStates.TeardownFailed,
                EphemeralEnvironmentStates.TornDown,
                ""
            })
            {
                ephStatesQueue.Enqueue(new List<EnvironmentApiModel> { new() { EnvironmentName = "eph-val", EphemeralState = failedState } });
                Assert.AreEqual(2, await valClient.ExecuteAsync(new EphemeralCliArgs(new[]
                {
                    "ephemeral", "up", "--project", "10", "--target-env", "eph-val", "--run-validation"
                })));
            }

            // Null list / non-matching environment name in GET /EphemeralEnvironments returns 2
            ephStatesQueue.Enqueue(null);
            Assert.AreEqual(2, await valClient.ExecuteAsync(new EphemeralCliArgs(new[]
            {
                "ephemeral", "up", "--project", "10", "--target-env", "eph-val", "--run-validation"
            })));

            // Timeout during WaitForEphemeralEnvironmentReadyAsync returns 2
            var valClock = DateTimeOffset.UtcNow;
            using var valTimeoutHttp = new HttpClient(new StubHttpMessageHandler(req =>
            {
                var p = req.RequestUri!.PathAndQuery;
                if (p == "/EphemeralEnvironments/provision")
                {
                    return JsonResponse(new EphemeralEnvironmentProvisionResponseApiModel
                    {
                        Environment = new EnvironmentApiModel { EnvironmentName = "eph-val-timeout" },
                        ProvisionRequestIds = new List<int> { 702 }
                    });
                }
                if (p == "/Request?id=702")
                {
                    valClock = valClock.AddMinutes(10);
                    return JsonResponse(new RequestStatusDto { Id = 702, Status = "Completed" });
                }
                return JsonResponse(new List<EnvironmentApiModel> { new() { EnvironmentName = "eph-val-timeout", EphemeralState = EphemeralEnvironmentStates.Testing } });
            })) { BaseAddress = new Uri("https://localhost/") };
            var valTimeoutClient = new EphemeralEnvironmentCliClient(valTimeoutHttp, logs.Add, TimeSpan.FromMilliseconds(1), () => valClock);
            Assert.AreEqual(2, await valTimeoutClient.ExecuteAsync(new EphemeralCliArgs(new[]
            {
                "ephemeral", "up", "--project", "10", "--target-env", "eph-val-timeout", "--run-validation", "--timeout-minutes", "5"
            })));

            using var downNoIdHttp = new HttpClient(new StubHttpMessageHandler(_ => JsonResponse(new EphemeralEnvironmentTeardownResponseApiModel
            {
                EnvironmentName = "eph-pr-1",
                EphemeralState = EphemeralEnvironmentStates.TornDown,
                DestroyRequestId = null
            }))) { BaseAddress = new Uri("https://localhost/") };
            var downNoIdClient = new EphemeralEnvironmentCliClient(downNoIdHttp);
            Assert.AreEqual(0, await downNoIdClient.ExecuteAsync(new EphemeralCliArgs(new[] { "ephemeral", "down", "--target-env", "eph-pr-1" })));
            Assert.AreEqual(0, await sut.ExecuteAsync(new EphemeralCliArgs(new[] { "ephemeral", "down", "--target-env", "eph-pr-1", "--no-wait" })));

            // Deferred teardown with --block when DestroyRequestIds is empty and EphemeralState is TearingDown:
            // polls RefDataEnvironments until TornDown / deleted (returns 0), TeardownFailed (returns 2), or timeout (returns 2)
            var deferredPollCount = 0;
            using var downDeferredCompleteHttp = new HttpClient(new StubHttpMessageHandler(req =>
            {
                var p = req.RequestUri!.PathAndQuery;
                if (p.EndsWith("/teardown", StringComparison.Ordinal))
                {
                    return JsonResponse(new EphemeralEnvironmentTeardownResponseApiModel
                    {
                        EnvironmentName = "eph-pr-1",
                        EphemeralState = EphemeralEnvironmentStates.TearingDown,
                        DestroyRequestId = null
                    });
                }
                deferredPollCount++;
                if (deferredPollCount == 1)
                {
                    return JsonResponse(new List<EnvironmentApiModel>
                    {
                        new() { EnvironmentName = "other-env", EphemeralState = EphemeralEnvironmentStates.Ready },
                        new() { EnvironmentName = "eph-pr-1", EphemeralState = EphemeralEnvironmentStates.TearingDown }
                    });
                }
                if (deferredPollCount == 2)
                {
                    return JsonResponse(new List<EnvironmentApiModel>
                    {
                        new() { EnvironmentName = "eph-pr-1", EphemeralState = EphemeralEnvironmentStates.TornDown }
                    });
                }
                if (deferredPollCount == 3)
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }
                return JsonResponse(new List<EnvironmentApiModel>());
            })) { BaseAddress = new Uri("https://localhost/") };
            var downDeferredCompleteClient = new EphemeralEnvironmentCliClient(downDeferredCompleteHttp, logs.Add, TimeSpan.FromMilliseconds(1));
            Assert.AreEqual(0, await downDeferredCompleteClient.ExecuteAsync(new EphemeralCliArgs(new[] { "ephemeral", "down", "--target-env", "eph-pr-1" })));
            Assert.AreEqual(0, await downDeferredCompleteClient.ExecuteAsync(new EphemeralCliArgs(new[] { "ephemeral", "down", "--target-env", "eph-pr-1" })));
            Assert.AreEqual(0, await downDeferredCompleteClient.ExecuteAsync(new EphemeralCliArgs(new[] { "ephemeral", "down", "--target-env", "eph-pr-1" })));

            using var downDeferredFailedHttp = new HttpClient(new StubHttpMessageHandler(req =>
            {
                var p = req.RequestUri!.PathAndQuery;
                if (p.EndsWith("/teardown", StringComparison.Ordinal))
                {
                    return JsonResponse(new EphemeralEnvironmentTeardownResponseApiModel
                    {
                        EnvironmentName = "eph-pr-1",
                        EphemeralState = EphemeralEnvironmentStates.TearingDown,
                        DestroyRequestId = null
                    });
                }
                return JsonResponse(new List<EnvironmentApiModel>
                {
                    new() { EnvironmentName = "eph-pr-1", EphemeralState = EphemeralEnvironmentStates.TeardownFailed }
                });
            })) { BaseAddress = new Uri("https://localhost/") };
            var downDeferredFailedClient = new EphemeralEnvironmentCliClient(downDeferredFailedHttp, logs.Add, TimeSpan.FromMilliseconds(1));
            Assert.AreEqual(2, await downDeferredFailedClient.ExecuteAsync(new EphemeralCliArgs(new[] { "ephemeral", "down", "--target-env", "eph-pr-1" })));

            var deferredClock = DateTimeOffset.UtcNow;
            using var downDeferredTimeoutHttp = new HttpClient(new StubHttpMessageHandler(req =>
            {
                var p = req.RequestUri!.PathAndQuery;
                if (p.EndsWith("/teardown", StringComparison.Ordinal))
                {
                    return JsonResponse(new EphemeralEnvironmentTeardownResponseApiModel
                    {
                        EnvironmentName = "eph-pr-1",
                        EphemeralState = EphemeralEnvironmentStates.TearingDown,
                        DestroyRequestId = null
                    });
                }
                deferredClock = deferredClock.AddMinutes(10);
                return JsonResponse(new List<EnvironmentApiModel>
                {
                    new() { EnvironmentName = "eph-pr-1", EphemeralState = EphemeralEnvironmentStates.TearingDown }
                });
            })) { BaseAddress = new Uri("https://localhost/") };
            var downDeferredTimeoutClient = new EphemeralEnvironmentCliClient(downDeferredTimeoutHttp, logs.Add, TimeSpan.FromMilliseconds(1), () => deferredClock);
            Assert.AreEqual(2, await downDeferredTimeoutClient.ExecuteAsync(new EphemeralCliArgs(new[] { "ephemeral", "down", "--target-env", "eph-pr-1", "--timeout-minutes", "5" })));

            using var downMultiFailHttp = new HttpClient(new StubHttpMessageHandler(req =>
            {
                var p = req.RequestUri!.PathAndQuery;
                if (p.EndsWith("/teardown", StringComparison.Ordinal))
                {
                    return JsonResponse(new EphemeralEnvironmentTeardownResponseApiModel
                    {
                        EnvironmentName = "eph-pr-1",
                        EphemeralState = EphemeralEnvironmentStates.TearingDown,
                        DestroyRequestId = 802,
                        DestroyRequestIds = new List<int> { 801, 802 }
                    });
                }
                if (p == "/Request?id=801")
                    return JsonResponse(new RequestStatusDto { Id = 801, Status = "Completed" });
                return JsonResponse(new RequestStatusDto { Id = 802, Status = "Failed" });
            })) { BaseAddress = new Uri("https://localhost/") };
            var downMultiFailClient = new EphemeralEnvironmentCliClient(downMultiFailHttp, logs.Add, TimeSpan.FromMilliseconds(1));
            Assert.AreEqual(2, await downMultiFailClient.ExecuteAsync(new EphemeralCliArgs(new[] { "ephemeral", "down", "--target-env", "eph-pr-1" })));

            // 6) Resolution failures for Project, Template, BaseEnvironment
            using var notFoundLookupHttp = new HttpClient(new StubHttpMessageHandler(req =>
            {
                var p = req.RequestUri!.PathAndQuery;
                if (p == "/RefDataProjects/MissingProj" || p == "/RefDataProjects/0")
                    return JsonResponse(new ProjectApiModel { ProjectId = 0 });
                if (p == "/RefDataProjects/NullProj")
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("null", Encoding.UTF8, "application/json") };
                if (p == "/EphemeralEnvironments/templates?projectId=10")
                    return JsonResponse(new List<EnvironmentTemplateApiModel>());
                if (p == "/EphemeralEnvironments/templates?projectId=11")
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("null", Encoding.UTF8, "application/json") };
                if (p == "/RefDataEnvironments?env=MissingBase" || p == "/RefDataEnvironments?env=0")
                    return JsonResponse(new List<EnvironmentApiModel>());
                if (p == "/RefDataEnvironments?env=NullBase")
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("null", Encoding.UTF8, "application/json") };
                return JsonResponse(new ProjectApiModel { ProjectId = 10 });
            })) { BaseAddress = new Uri("https://localhost/") };
            var lookupClient = new EphemeralEnvironmentCliClient(notFoundLookupHttp);

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                lookupClient.ExecuteAsync(new EphemeralCliArgs(new[] { "ephemeral", "up", "--project", "MissingProj", "--target-env", "e" })));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                lookupClient.ExecuteAsync(new EphemeralCliArgs(new[] { "ephemeral", "up", "--project", "NullProj", "--target-env", "e" })));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                lookupClient.ExecuteAsync(new EphemeralCliArgs(new[] { "ephemeral", "up", "--project", "0", "--target-env", "e" })));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                lookupClient.ExecuteAsync(new EphemeralCliArgs(new[] { "ephemeral", "up", "--project", "10", "--template", "MissingTpl", "--target-env", "e" })));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                lookupClient.ExecuteAsync(new EphemeralCliArgs(new[] { "ephemeral", "up", "--project", "11", "--template", "0", "--target-env", "e" })));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                lookupClient.ExecuteAsync(new EphemeralCliArgs(new[] { "ephemeral", "up", "--project", "10", "--base-env", "MissingBase", "--target-env", "e" })));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                lookupClient.ExecuteAsync(new EphemeralCliArgs(new[] { "ephemeral", "up", "--project", "10", "--base-env", "0", "--target-env", "e" })));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                lookupClient.ExecuteAsync(new EphemeralCliArgs(new[] { "ephemeral", "up", "--project", "10", "--base-env", "NullBase", "--target-env", "e" })));
        }

        [TestMethod]
        public void Program_Main_CoversEphemeralAndStandardDeployFlows()
        {
            var originalConfigFactory = Program.ConfigurationFactory;
            var originalApiFactory = Program.ApiCallerFactory;
            var originalEphFactory = Program.EphemeralClientFactory;
            var originalTokenFactory = Program.TokenProviderFactory;
            var originalInteractive = Program.IsUserInteractive;
            var originalReadKey = Program.ReadKeyAction;

            try
            {
                var config = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["DorcApi:BaseUrl"] = "https://localhost" })
                    .Build();

                // Cover default EphemeralClientFactory creation (with and without DorcApi:BaseUrl, and with OAuth ClientId/ClientSecret)
                Assert.IsNotNull(Program.EphemeralClientFactory(config, _ => { }));
                var emptyConfig = new ConfigurationBuilder().Build();
                Assert.IsNotNull(Program.EphemeralClientFactory(emptyConfig, _ => { }));

                var oauthConfig = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["DorcApi:BaseUrl"] = "http://127.0.0.1:1",
                        ["DorcApi:ClientId"] = "client-id",
                        ["DorcApi:ClientSecret"] = "client-secret"
                    })
                    .Build();

                // Cover default TokenProviderFactory failure fallback when token endpoint is unreachable
                using (var fallbackClient = Program.CreateAuthenticatedHttpClient(oauthConfig))
                {
                    Assert.IsNull(fallbackClient.DefaultRequestHeaders.Authorization);
                }

                // Cover partial OAuth config (ClientId present, ClientSecret missing)
                var partialOAuthConfig = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["DorcApi:BaseUrl"] = "https://localhost",
                        ["DorcApi:ClientId"] = "client-id"
                    })
                    .Build();
                using (var partialClient = Program.CreateAuthenticatedHttpClient(partialOAuthConfig))
                {
                    Assert.IsNull(partialClient.DefaultRequestHeaders.Authorization);
                }

                // Cover TokenProviderFactory returning a bearer token
                Program.TokenProviderFactory = _ => "test-jwt-token";
                using (var authClient = Program.CreateAuthenticatedHttpClient(oauthConfig))
                {
                    Assert.IsNotNull(authClient.DefaultRequestHeaders.Authorization);
                    Assert.AreEqual("Bearer", authClient.DefaultRequestHeaders.Authorization!.Scheme);
                    Assert.AreEqual("test-jwt-token", authClient.DefaultRequestHeaders.Authorization.Parameter);
                }

                // Cover TokenProviderFactory returning an empty token
                Program.TokenProviderFactory = _ => string.Empty;
                using (var emptyTokenClient = Program.CreateAuthenticatedHttpClient(oauthConfig))
                {
                    Assert.IsNull(emptyTokenClient.DefaultRequestHeaders.Authorization);
                }

                // Cover CreateAuthenticatedHttpClient without OAuth but with custom innerHandler
                using (var customInnerClient = Program.CreateAuthenticatedHttpClient(
                    partialOAuthConfig,
                    new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))))
                {
                    Assert.IsNull(customInnerClient.DefaultRequestHeaders.Authorization);
                }

                var buildTextArgs = new EphemeralCliArgs(new[]
                {
                    "ephemeral", "up",
                    "--project", "P",
                    "--target-env", "eph-bt",
                    "--build-text", "ci.yml"
                });
                Assert.AreEqual("ci.yml", buildTextArgs.BuildText);

                // Cover BearerTokenRefreshingHandler: initial token, 401 refresh with non-empty/empty token, and exception logging
                var tokenCalls = 0;
                Program.TokenProviderFactory = _ =>
                {
                    tokenCalls++;
                    return tokenCalls == 1 ? "tok-1" : "tok-2";
                };
                var sendCount = 0;
                string? retriedToken = null;
                using (var refreshClient = Program.CreateAuthenticatedHttpClient(
                    oauthConfig,
                    new StubHttpMessageHandler(req =>
                    {
                        sendCount++;
                        if (sendCount == 1)
                        {
                            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                        }
                        retriedToken = req.Headers.Authorization?.Parameter;
                        return new HttpResponseMessage(HttpStatusCode.OK);
                    })))
                {
                    var resp = refreshClient.GetAsync("/test").GetAwaiter().GetResult();
                    Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
                    Assert.AreEqual("tok-2", retriedToken);

                    // Second request reuses cached _cachedToken ("tok-2") without calling TokenProviderFactory again when 200 OK
                    var resp2 = refreshClient.GetAsync("/test").GetAwaiter().GetResult();
                    Assert.AreEqual(HttpStatusCode.OK, resp2.StatusCode);
                }

                // Cover BearerTokenRefreshingHandler when initial token is empty, then 401 refresh returns empty, and constructor guard clauses
                Program.TokenProviderFactory = _ => null;
                using (var emptyRefreshClient = Program.CreateAuthenticatedHttpClient(
                    oauthConfig,
                    new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized))))
                {
                    var resp = emptyRefreshClient.GetAsync("/test").GetAwaiter().GetResult();
                    Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
                }

                Assert.ThrowsExactly<ArgumentNullException>(() => new BearerTokenRefreshingHandler(null!, _ => "t"));
                Assert.ThrowsExactly<ArgumentNullException>(() => new BearerTokenRefreshingHandler(new Dorc.Core.Configuration.DorcOAuthClientConfiguration(oauthConfig), null!));
                using (var defaultHandler = new BearerTokenRefreshingHandler(new Dorc.Core.Configuration.DorcOAuthClientConfiguration(oauthConfig), _ => "t"))
                {
                    Assert.AreEqual("t", defaultHandler.TryAcquireToken());
                }

                Program.ConfigurationFactory = () => config;
                var readKeyCalled = false;
                Program.IsUserInteractive = () => true;
                Program.ReadKeyAction = () => readKeyCalled = true;

                // 1) Ephemeral command success (does not wait for ReadKey)
                Program.EphemeralClientFactory = (_, output) =>
                {
                    output("ephemeral stub executed");
                    return new StubEphemeralClient(0);
                };
                Assert.AreEqual(0, Program.Main(new[] { "ephemeral", "down", "--target-env", "eph-1" }));
                Assert.IsFalse(readKeyCalled);

                // 2) Standard deploy with IsUserInteractive = true calls ReadKeyAction, and Wait = true / Wait = false
                Program.ApiCallerFactory = _ => new StubApiCaller(
                    postResult: new ApiResult<RequestStatusDto> { IsModelValid = true, Value = new RequestStatusDto { Id = 77, Status = "Completed" } },
                    getResult: new ApiResult<RequestStatusDto> { IsModelValid = true, Value = new RequestStatusDto { Id = 77, Status = "Completed" } });
                Assert.AreEqual(2, Program.Main(new[] { "/project:P", "/targetenv:E", "/buildnum:1.0", "/components:C1", "/wait:true" }));
                Assert.IsTrue(readKeyCalled);

                Program.IsUserInteractive = () => false;

                // 3) Standard deploy with Wait = false
                Assert.AreEqual(0, Program.Main(new[] { "/project:P", "/targetenv:E", "/buildnum:1.0", "/components:C1", "/wait:false" }));

                // 4) Standard deploy with invalid model / Id <= 0 / exception in standard deploy
                Program.ApiCallerFactory = _ => new StubApiCaller(
                    postResult: new ApiResult<RequestStatusDto> { IsModelValid = false, ErrorMessage = "bad" },
                    getResult: new ApiResult<RequestStatusDto>());
                Assert.AreEqual(1, Program.Main(new[] { "/project:P", "/targetenv:E", "/buildnum:1.0", "/components:C1" }));

                Program.ApiCallerFactory = _ => new StubApiCaller(
                    postResult: new ApiResult<RequestStatusDto> { IsModelValid = true, Value = new RequestStatusDto { Id = 0 } },
                    getResult: new ApiResult<RequestStatusDto>());
                Assert.AreEqual(1, Program.Main(new[] { "/project:P", "/targetenv:E", "/buildnum:1.0", "/components:C1" }));

                Program.ApiCallerFactory = _ => throw new InvalidOperationException("StandardDeployBoom");
                Assert.AreEqual(1, Program.Main(new[] { "/project:P", "/targetenv:E", "/buildnum:1.0", "/components:C1" }));

                // 5) Ephemeral exception with inner exception + without inner exception + CatchAllUnhandledExceptions
                Program.EphemeralClientFactory = (_, _) => throw new InvalidOperationException("Outer", new ArgumentException("Inner"));
                Assert.AreEqual(1, Program.Main(new[] { "ephemeral", "down", "--target-env", "eph-1" }));

                Program.EphemeralClientFactory = (_, _) => throw new InvalidOperationException("OuterOnly");
                Assert.AreEqual(1, Program.Main(new[] { "ephemeral", "down", "--target-env", "eph-1" }));

                Program.CatchAllUnhandledExceptions(this, new UnhandledExceptionEventArgs(new Exception("Unhandled"), false));

                // 6) Ephemeral extend-ttl parsing and execution (both success and error)
                var extendArgs = new EphemeralCliArgs(new[] { "ephemeral", "extend-ttl", "--target-env", "eph-1", "--additional-minutes", "90" });
                Assert.AreEqual(EphemeralCliVerb.ExtendTtl, extendArgs.Verb);
                Assert.AreEqual(90, extendArgs.TtlMinutes);

                var extendShortArgs = new EphemeralCliArgs(new[] { "ephemeral", "extend-ttl", "/targetenv:eph-1", "/additionalminutes:45" });
                Assert.AreEqual(45, extendShortArgs.TtlMinutes);

                Assert.ThrowsExactly<ArgumentException>(() =>
                    new EphemeralCliArgs(new[] { "ephemeral", "extend-ttl", "--target-env", "eph-1" }));

                using (var okExtendHttp = new HttpClient(new StubHttpMessageHandler(_ => JsonResponse(new EnvironmentApiModel { EnvironmentName = "eph-1" })))
                {
                    BaseAddress = new Uri("https://localhost/")
                })
                {
                    var okExtendClient = new EphemeralEnvironmentCliClient(okExtendHttp);
                    Assert.AreEqual(0, okExtendClient.ExecuteAsync(extendArgs).GetAwaiter().GetResult());
                }

                using (var errExtendHttp = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("policy cap exceeded")
                }))
                {
                    BaseAddress = new Uri("https://localhost/")
                })
                {
                    var errExtendClient = new EphemeralEnvironmentCliClient(errExtendHttp);
                    Assert.AreEqual(1, errExtendClient.ExecuteAsync(extendArgs).GetAwaiter().GetResult());
                }
            }
            finally
            {
                Program.ConfigurationFactory = originalConfigFactory;
                Program.ApiCallerFactory = originalApiFactory;
                Program.EphemeralClientFactory = originalEphFactory;
                Program.TokenProviderFactory = originalTokenFactory;
                Program.IsUserInteractive = originalInteractive;
                Program.ReadKeyAction = originalReadKey;
            }
        }

        [TestMethod]
        public async Task EphemeralBlueprintCommands_ParseAndExecute_CoversListExportAndApply()
        {
            // 1) Parsing both two-token ("ephemeral blueprint list") and hyphenated ("ephemeral blueprint-list") forms
            var listArgs = new EphemeralCliArgs(new[] { "ephemeral", "blueprint", "list", "--project", "MyProj" });
            Assert.AreEqual(EphemeralCliVerb.BlueprintList, listArgs.Verb);
            Assert.AreEqual("MyProj", listArgs.Project);

            var listHyphenArgs = new EphemeralCliArgs(new[] { "ephemeral", "blueprint-list", "/project:MyProj" });
            Assert.AreEqual(EphemeralCliVerb.BlueprintList, listHyphenArgs.Verb);
            Assert.AreEqual("MyProj", listHyphenArgs.Project);

            var exportArgs = new EphemeralCliArgs(new[]
            {
                "ephemeral", "blueprint", "export",
                "--project", "MyProj",
                "--template", "Fast PR",
                "--file", "out.json"
            });
            Assert.AreEqual(EphemeralCliVerb.BlueprintExport, exportArgs.Verb);
            Assert.AreEqual("MyProj", exportArgs.Project);
            Assert.AreEqual("Fast PR", exportArgs.Template);
            Assert.AreEqual("out.json", exportArgs.FilePath);

            var exportAliasArgs = new EphemeralCliArgs(new[]
            {
                "ephemeral", "blueprint-export",
                "/project:MyProj",
                "/template:Fast PR",
                "/output:out2.json"
            });
            Assert.AreEqual(EphemeralCliVerb.BlueprintExport, exportAliasArgs.Verb);
            Assert.AreEqual("out2.json", exportAliasArgs.FilePath);

            var applyArgs = new EphemeralCliArgs(new[]
            {
                "ephemeral", "blueprint", "apply",
                "--project", "MyProj",
                "--file", "blueprints.json"
            });
            Assert.AreEqual(EphemeralCliVerb.BlueprintApply, applyArgs.Verb);
            Assert.AreEqual("MyProj", applyArgs.Project);
            Assert.AreEqual("blueprints.json", applyArgs.FilePath);

            var applyHyphenNoProjectArgs = new EphemeralCliArgs(new[]
            {
                "ephemeral", "blueprint-apply",
                "/file:blueprints.json"
            });
            Assert.AreEqual(EphemeralCliVerb.BlueprintApply, applyHyphenNoProjectArgs.Verb);
            Assert.AreEqual(string.Empty, applyHyphenNoProjectArgs.Project);
            Assert.AreEqual("blueprints.json", applyHyphenNoProjectArgs.FilePath);

            // Validation errors for blueprint sub-commands
            Assert.ThrowsExactly<ArgumentException>(() => new EphemeralCliArgs(new[] { "ephemeral", "blueprint" }));
            Assert.ThrowsExactly<ArgumentException>(() => new EphemeralCliArgs(new[] { "ephemeral", "blueprint", "unknown" }));
            Assert.ThrowsExactly<ArgumentException>(() => new EphemeralCliArgs(new[] { "ephemeral", "blueprint", "list" }));
            Assert.ThrowsExactly<ArgumentException>(() => new EphemeralCliArgs(new[] { "ephemeral", "blueprint", "export", "--template", "T" }));
            Assert.ThrowsExactly<ArgumentException>(() => new EphemeralCliArgs(new[] { "ephemeral", "blueprint", "export", "--project", "P" }));
            Assert.ThrowsExactly<ArgumentException>(() => new EphemeralCliArgs(new[] { "ephemeral", "blueprint", "apply" }));

            // 2) Execute BlueprintList, BlueprintExport, and BlueprintApply against mock HttpMessageHandler
            var logs = new List<string>();
            EnvironmentBlueprintImportRequestApiModel? lastImportedRequest = null;

            var handler = new StubHttpMessageHandler(req =>
            {
                var path = req.RequestUri!.PathAndQuery;
                if (path == "/RefDataProjects/MyProj")
                {
                    return JsonResponse(new ProjectApiModel { ProjectId = 42, ProjectName = "MyProj" });
                }
                if (path == "/EphemeralEnvironments/templates?projectId=42")
                {
                    return JsonResponse(new List<EnvironmentTemplateApiModel>
                    {
                        new()
                        {
                            Id = 7,
                            ProjectId = 42,
                            ProjectName = "MyProj",
                            Name = "Fast PR",
                            DefaultTier = "xx",
                            BaseEnvironmentName = "TRV-DV-01",
                            DefaultTtlMinutes = 240,
                            MaxTtlMinutes = 720,
                            ProvisionComponentIds = new List<int> { 1, 2 },
                            ValidationComponentIds = new List<int> { 3 }
                        }
                    });
                }
                if (path == "/EphemeralEnvironments/templates/7/export")
                {
                    return JsonResponse(new EnvironmentBlueprintDefinitionApiModel
                    {
                        SchemaVersion = "1.0",
                        ProjectName = "MyProj",
                        Name = "Fast PR",
                        Description = "Fast PR sandbox",
                        DefaultTier = "xx",
                        BaseEnvironmentName = "TRV-DV-01",
                        DefaultTtlMinutes = 240,
                        MaxTtlMinutes = 720,
                        ProvisionComponentNames = new List<string> { "CompA", "CompB" },
                        ValidationComponentNames = new List<string> { "ValA" }
                    });
                }
                if (path == "/EphemeralEnvironments/templates/import")
                {
                    var bodyJson = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    lastImportedRequest = JsonSerializer.Deserialize<EnvironmentBlueprintImportRequestApiModel>(
                        bodyJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    return JsonResponse(new EnvironmentBlueprintImportResponseApiModel
                    {
                        ImportedTemplates = new List<EnvironmentTemplateApiModel>
                        {
                            new() { Id = 7, Name = "Fast PR" }
                        },
                        CreatedCount = 1,
                        UpdatedCount = 0
                    });
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
            var client = new EphemeralEnvironmentCliClient(httpClient, logs.Add);

            // BlueprintList
            var listExitCode = await client.ExecuteAsync(listArgs);
            Assert.AreEqual(0, listExitCode);
            Assert.IsTrue(logs.Exists(l => l.Contains("Fast PR", StringComparison.Ordinal) && l.Contains("TRV-DV-01", StringComparison.Ordinal)));

            // BlueprintExport to stdout (no --file)
            logs.Clear();
            var exportStdoutExitCode = await client.ExecuteAsync(new EphemeralCliArgs(new[]
            {
                "ephemeral", "blueprint", "export",
                "--project", "MyProj",
                "--template", "Fast PR"
            }));
            Assert.AreEqual(0, exportStdoutExitCode);
            Assert.IsTrue(logs.Exists(l => l.Contains("\"Fast PR\"", StringComparison.Ordinal)));

            // BlueprintExport to file and BlueprintApply from file (single object, array, and wrapper object)
            var testArtifactPath = System.IO.Path.Combine(AppContext.BaseDirectory, $"test-blueprint-{Guid.NewGuid():N}.json");
            try
            {
                var exportToFileArgs = new EphemeralCliArgs(new[]
                {
                    "ephemeral", "blueprint", "export",
                    "--project", "MyProj",
                    "--template", "Fast PR",
                    "--file", testArtifactPath
                });
                Assert.AreEqual(0, await client.ExecuteAsync(exportToFileArgs));
                Assert.IsTrue(System.IO.File.Exists(testArtifactPath));

                // Apply single blueprint definition object with --project override
                logs.Clear();
                var applySingleExit = await client.ExecuteAsync(new EphemeralCliArgs(new[]
                {
                    "ephemeral", "blueprint", "apply",
                    "--project", "MyProj",
                    "--file", testArtifactPath
                }));
                Assert.AreEqual(0, applySingleExit);
                Assert.IsNotNull(lastImportedRequest);
                Assert.AreEqual("MyProj", lastImportedRequest!.ProjectName);
                Assert.HasCount(1, lastImportedRequest.Blueprints);
                Assert.AreEqual("Fast PR", lastImportedRequest.Blueprints[0].Name);
                Assert.IsTrue(logs.Exists(l => l.Contains("Applied 1 blueprint(s) (1 created, 0 updated).", StringComparison.Ordinal)));

                // Apply array of blueprint definitions without --project (falls back to blueprint's ProjectName)
                await System.IO.File.WriteAllTextAsync(testArtifactPath, JsonSerializer.Serialize(new List<EnvironmentBlueprintDefinitionApiModel>
                {
                    new() { ProjectName = "FileFallbackProj", Name = "BpFromArray" }
                }));
                Assert.AreEqual(0, await client.ExecuteAsync(new EphemeralCliArgs(new[]
                {
                    "ephemeral", "blueprint", "apply",
                    "--file", testArtifactPath
                })));
                Assert.AreEqual("FileFallbackProj", lastImportedRequest!.ProjectName);
                Assert.HasCount(1, lastImportedRequest.Blueprints);

                // Apply EnvironmentBlueprintImportRequestApiModel wrapper with numeric --project ID
                await System.IO.File.WriteAllTextAsync(testArtifactPath, JsonSerializer.Serialize(new EnvironmentBlueprintImportRequestApiModel
                {
                    ProjectName = "WrapperProj",
                    Blueprints = new List<EnvironmentBlueprintDefinitionApiModel>
                    {
                        new() { Name = "BpFromWrapper" }
                    }
                }));
                Assert.AreEqual(0, await client.ExecuteAsync(new EphemeralCliArgs(new[]
                {
                    "ephemeral", "blueprint", "apply",
                    "--project", "42",
                    "--file", testArtifactPath
                })));
                Assert.AreEqual(42, lastImportedRequest!.ProjectId);
                Assert.AreEqual("BpFromWrapper", lastImportedRequest.Blueprints[0].Name);
            }
            finally
            {
                if (System.IO.File.Exists(testArtifactPath))
                {
                    System.IO.File.Delete(testArtifactPath);
                }
            }

            // BlueprintApply when file does not exist returns 1
            var missingFileExit = await client.ExecuteAsync(new EphemeralCliArgs(new[]
            {
                "ephemeral", "blueprint", "apply",
                "--project", "MyProj",
                "--file", "nonexistent-blueprint-file-xyz.json"
            }));
            Assert.AreEqual(1, missingFileExit);
        }

        private static HttpResponseMessage JsonResponse<T>(T value)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
            };
        }

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

            public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            {
                _responder = responder;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(_responder(request));
            }
        }

        private sealed class StubEphemeralClient : IEphemeralEnvironmentCliClient
        {
            private readonly int _exitCode;

            public StubEphemeralClient(int exitCode)
            {
                _exitCode = exitCode;
            }

            public Task<int> ExecuteAsync(EphemeralCliArgs cliArgs, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(_exitCode);
            }
        }

        private sealed class StubApiCaller : IApiCaller
        {
            private readonly ApiResult<RequestStatusDto> _postResult;
            private readonly ApiResult<RequestStatusDto> _getResult;

            public StubApiCaller(ApiResult<RequestStatusDto> postResult, ApiResult<RequestStatusDto> getResult)
            {
                _postResult = postResult;
                _getResult = getResult;
            }

            public ApiResult<T> Call<T>(Endpoints endpoint, Method method, Dictionary<string, string>? segments = null, string? body = null) where T : class
            {
                object res = method == Method.Post ? _postResult : _getResult;
                return (ApiResult<T>)res;
            }
        }
    }
}
