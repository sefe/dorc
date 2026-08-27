using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.Configuration;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Security.Claims;

namespace Dorc.Core.Tests
{
    // S-005: DaemonStatusProbe is platform-neutral orchestration; the Windows-only
    // probing/service-control half sits behind IDaemonOperations. These tests pin the
    // seam contract: when the seam is called, with what credential, and that observation
    // recording follows the seam's results.
    [TestClass]
    public class DaemonStatusProbeTests
    {
        private IConfigValuesPersistentSource _configValues = null!;
        private IEnvironmentsPersistentSource _environments = null!;
        private IServersPersistentSource _servers = null!;
        private IDaemonsPersistentSource _daemons = null!;
        private IDaemonObservationPersistentSource _observations = null!;
        private IDaemonOperations _operations = null!;
        private DaemonStatusProbe _probe = null!;

        [TestInitialize]
        public void Setup()
        {
            _configValues = Substitute.For<IConfigValuesPersistentSource>();
            _environments = Substitute.For<IEnvironmentsPersistentSource>();
            _servers = Substitute.For<IServersPersistentSource>();
            _daemons = Substitute.For<IDaemonsPersistentSource>();
            _observations = Substitute.For<IDaemonObservationPersistentSource>();
            _operations = Substitute.For<IDaemonOperations>();

            var config = Substitute.For<IConfigurationSettings>();
            config.GetConfigurationDomainNameIntra().Returns("CORP");

            _probe = new DaemonStatusProbe(
                _configValues,
                NullLogger<DaemonStatusProbe>.Instance,
                _environments,
                _servers,
                _daemons,
                _observations,
                config,
                Substitute.For<IDaemonAuditPersistentSource>(),
                Substitute.For<IServersAuditPersistentSource>(),
                Substitute.For<IClaimsPrincipalReader>(),
                _operations);
        }

        private void SetupEnvironment(bool isProd = false)
        {
            var environment = new EnvironmentApiModel
            {
                EnvironmentId = 7,
                EnvironmentName = "TestEnv",
                EnvironmentIsProd = isProd
            };
            _environments.GetEnvironment(7, Arg.Any<ClaimsPrincipal>()).Returns(environment);

            _servers.GetServersForEnvId(7).Returns(new List<ServerApiModel>
            {
                new() { ServerId = 11, Name = "SRV01" }
            });
            _daemons.GetDaemonsForServer(11).Returns(new List<DaemonApiModel>
            {
                new() { Id = 21, Name = "DorcMonitor" }
            });
        }

        [TestMethod]
        public void NoDeployCredential_ReturnsUnprobedList_WithoutTouchingTheSeam()
        {
            SetupEnvironment();
            _configValues.GetConfigValue("DORC_NonProdDeployUsername").Returns((string?)null);
            _configValues.GetConfigValue("DORC_NonProdDeployPassword").Returns((string?)null);

            var result = _probe.GetDaemonStatuses(7);

            Assert.AreEqual(1, result.Count);
            Assert.IsNull(result[0].Status);
            _operations.DidNotReceiveWithAnyArgs().ProbeStatuses(default, default!);
            _observations.DidNotReceiveWithAnyArgs().InsertObservation(default, default, default, default, default);
        }

        [TestMethod]
        public void DeployCredentialConfigured_ProbesViaSeam_AndRecordsObservations()
        {
            SetupEnvironment();
            _configValues.GetConfigValue("DORC_NonProdDeployUsername").Returns("svc_deploy");
            _configValues.GetConfigValue("DORC_NonProdDeployPassword").Returns("pw");

            WorkerDaemonCredentialApiModel? seenCredential = null;
            _operations.ProbeStatuses(Arg.Any<WorkerDaemonCredentialApiModel?>(), Arg.Any<List<DaemonStatus>>())
                .Returns(ci =>
                {
                    seenCredential = ci.Arg<WorkerDaemonCredentialApiModel?>();
                    return new List<DaemonStatus>
                    {
                        new() { ServerName = "SRV01", DaemonName = "DorcMonitor", Status = "Running", ServerId = 11, DaemonId = 21 }
                    };
                });

            var result = _probe.GetDaemonStatuses(7);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Running", result[0].Status);
            Assert.IsNotNull(seenCredential);
            Assert.AreEqual("svc_deploy", seenCredential!.Username);
            Assert.AreEqual("CORP", seenCredential.Domain);
            _observations.Received(1).InsertObservation(11, 21, Arg.Any<DateTime>(), "Running", null);
        }

        [TestMethod]
        public void ProdEnvironment_UsesProdDeployCredential()
        {
            SetupEnvironment(isProd: true);
            _configValues.GetConfigValue("DORC_ProdDeployUsername").Returns("svc_prod");
            _configValues.GetConfigValue("DORC_ProdDeployPassword").Returns("pw");
            _operations.ProbeStatuses(Arg.Any<WorkerDaemonCredentialApiModel?>(), Arg.Any<List<DaemonStatus>>())
                .Returns(new List<DaemonStatus>());

            _probe.GetDaemonStatuses(7);

            _operations.Received(1).ProbeStatuses(
                Arg.Is<WorkerDaemonCredentialApiModel?>(c => c!.Username == "svc_prod"),
                Arg.Any<List<DaemonStatus>>());
            _configValues.DidNotReceive().GetConfigValue("DORC_NonProdDeployUsername");
        }

        [TestMethod]
        public void ChangeDaemonState_AlwaysPassesTheDeployCredential()
        {
            var environment = new EnvironmentApiModel { EnvironmentId = 7, EnvironmentName = "TestEnv", EnvironmentIsProd = false };
            _environments.GetEnvironment("TestEnv", Arg.Any<ClaimsPrincipal>()).Returns(environment);
            _configValues.GetConfigValue("DORC_NonProdDeployUsername").Returns("svc_deploy");
            _configValues.GetConfigValue("DORC_NonProdDeployPassword").Returns("pw");

            var request = new DaemonStatus { EnvName = "TestEnv", ServerName = "SRV01", DaemonName = "DorcMonitor", Status = "Starting" };
            var changed = new DaemonStatus { Status = "Running" };
            _operations.ChangeState(Arg.Any<WorkerDaemonCredentialApiModel?>(), request).Returns(changed);

            var result = _probe.ChangeDaemonState(request, new ClaimsPrincipal());

            Assert.AreSame(changed, result);
            _operations.Received(1).ChangeState(
                Arg.Is<WorkerDaemonCredentialApiModel?>(c => c!.Username == "svc_deploy" && c.Domain == "CORP"),
                request);
        }
    }
}
