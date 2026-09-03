using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.VariableResolution;
using Dorc.PersistentData.Sources.Interfaces;
using NSubstitute;

namespace Dorc.Core.Tests
{
    /// <summary>
    /// Characterization baseline for HLPS SC-6 (docs/env-details-component-tabs/):
    /// freezes the exact SetPropertyValue call-set SetPropertyValues emits today.
    /// When the resolver's constructor grows new sources (IS step S-005), the only
    /// permitted edit here is stub wiring for those sources returning empty; the
    /// recorded call-set assertions must remain unchanged.
    /// </summary>
    [TestClass]
    public class VariableScopeOptionsResolverCharacterizationTests
    {
        private IPropertiesPersistentSource _properties = null!;
        private IServersPersistentSource _servers = null!;
        private IDaemonsPersistentSource _daemons = null!;
        private IDatabasesPersistentSource _databases = null!;
        private IUserPermsPersistentSource _userPerms = null!;
        // S-005 wiring only: the resolver's constructor grew three component sources;
        // they are stubbed to return empty so the frozen call-set assertions below are
        // untouched (conditional emission keeps the resolver silent for empty sources).
        private IContainersPersistentSource _containers = null!;
        private ICloudResourcesPersistentSource _cloudResources = null!;
        private IApiRegistrationsPersistentSource _apiRegistrations = null!;
        private List<(string Name, object? Value)> _calls = null!;

        [TestInitialize]
        public void Init()
        {
            _properties = Substitute.For<IPropertiesPersistentSource>();
            _servers = Substitute.For<IServersPersistentSource>();
            _daemons = Substitute.For<IDaemonsPersistentSource>();
            _databases = Substitute.For<IDatabasesPersistentSource>();
            _userPerms = Substitute.For<IUserPermsPersistentSource>();
            _containers = Substitute.For<IContainersPersistentSource>();
            _containers.GetForEnvironmentId(Arg.Any<int>()).Returns(Array.Empty<ContainerApiModel>());
            _cloudResources = Substitute.For<ICloudResourcesPersistentSource>();
            _cloudResources.GetForEnvironmentId(Arg.Any<int>()).Returns(Array.Empty<CloudResourceApiModel>());
            _apiRegistrations = Substitute.For<IApiRegistrationsPersistentSource>();
            _apiRegistrations.GetForEnvironmentId(Arg.Any<int>()).Returns(Array.Empty<ApiRegistrationApiModel>());
            _calls = new List<(string, object?)>();
        }

        private VariableScopeOptionsResolver CreateResolver() =>
            new(_properties, _servers, _daemons, _databases, _userPerms,
                _containers, _cloudResources, _apiRegistrations);

        private IVariableResolver CreateRecordingVariableResolver()
        {
            var variableResolver = Substitute.For<IVariableResolver>();
            variableResolver
                .When(v => v.SetPropertyValue(Arg.Any<string>(), Arg.Any<VariableValue?>()))
                .Do(ci => _calls.Add((ci.ArgAt<string>(0), ci.ArgAt<VariableValue?>(1))));
            variableResolver
                .When(v => v.SetPropertyValue(Arg.Any<string>(), Arg.Any<string>()))
                .Do(ci => _calls.Add((ci.ArgAt<string>(0), ci.ArgAt<string>(1))));
            return variableResolver;
        }

        private static EnvironmentApiModel BuildEnvironment(List<string>? ownerEmails) =>
            new()
            {
                EnvironmentId = 42,
                EnvironmentName = "IAR DV 07",
                Details = new EnvironmentDetailsApiModel
                {
                    FileShare = @"\\share\dv07",
                    EnvironmentOwnerEmails = ownerEmails
                }
            };

        [TestMethod]
        public void SetPropertyValues_RepresentativeEnvironment_EmitsFrozenCallSet()
        {
            var environment = BuildEnvironment(new List<string> { "owner@example.com" });

            _servers.GetServersForEnvId(42).Returns(new[]
            {
                new ServerApiModel
                {
                    ServerId = 1, Name = "web01", OsName = "Win2022",
                    ApplicationTags = "appserv;web tier"
                },
                new ServerApiModel
                {
                    ServerId = 2, Name = "web02", OsName = "Win2022",
                    ApplicationTags = "web tier"
                }
            });
            _daemons.GetDaemonsForServer(1).Returns(new[]
            {
                new DaemonApiModel
                {
                    Name = "EndurSvc", DisplayName = "Endur Service",
                    AccountName = @"DOM\svc", ServiceType = "WindowsService"
                }
            });
            _daemons.GetDaemonsForServer(2).Returns(Array.Empty<DaemonApiModel>());

            var endurDb = new DatabaseApiModel
            { Id = 10, Name = "END_DB_DV07", Type = "Endur", ServerName = "sql01" };
            var reportingDb = new DatabaseApiModel
            { Id = 11, Name = "REP_DB", Type = "Endur Reporting", ServerName = "sql02" };
            // Two databases sharing a type exercise the array variants of DbServer_/DbName_.
            var auditDb1 = new DatabaseApiModel
            { Id = 12, Name = "AUD1", Type = "Audit", ServerName = "sql03" };
            var auditDb2 = new DatabaseApiModel
            { Id = 13, Name = "AUD2", Type = "Audit", ServerName = "sql03" };
            var externalDb = new DatabaseApiModel
            { Id = 14, Name = "EXT_DB", Type = "Endur External", ServerName = "sql04" };
            _databases.GetDatabasesForEnvironmentName("IAR DV 07")
                .Returns(new[] { endurDb, reportingDb, auditDb1, auditDb2, externalDb });
            _databases.GetDatabaseByType(environment, "Endur").Returns(endurDb);

            _properties.GetConfigurationFilePath(environment).Returns("cfg/path");

            _userPerms.GetPermissions(10).Returns(new[]
            {
                new UserPermDto { User = @"DOM\alice", Role = "db_owner" },
                new UserPermDto { User = @"DOM\alice", Role = "db_datareader" }
            });
            _userPerms.GetPermissions(11).Returns(Array.Empty<UserPermDto>());
            _userPerms.GetPermissions(12).Returns(Array.Empty<UserPermDto>());
            _userPerms.GetPermissions(13).Returns(Array.Empty<UserPermDto>());
            _userPerms.GetPermissions(14).Returns(Array.Empty<UserPermDto>());

            CreateResolver().SetPropertyValues(CreateRecordingVariableResolver(), environment);

            var expectedNames = new[]
            {
                "appserv;web tier",
                "web tier",
                "AllServers",
                "EnvironmentServers",
                "EndurFileShare",
                "EndurConfigurationFile",
                "EndurDatabaseName",
                "EndurDatabaseServer",
                "EnvironmentShortName",
                "ReportingDatabaseName",
                "ReportingDatabaseServer",
                "SsisPackageServer",
                "ExternalDatabaseName",
                "ExternalDatabaseServer",
                "DbServer_Endur",
                "DbName_Endur",
                "DbServer_Endur_Reporting",
                "DbName_Endur_Reporting",
                "DbServer_Audit",
                "DbName_Audit",
                "DbServer_Endur_External",
                "DbName_Endur_External",
                "ServerNames_appserv",
                "ServerNames_web_tier",
                "DatabasePermissions",
                "EnvOwnerEmails"
            };
            CollectionAssert.AreEqual(expectedNames, _calls.Select(c => c.Name).ToArray(),
                $"Emitted call sequence changed. Actual: [{string.Join(", ", _calls.Select(c => c.Name))}]");

            // Per-server tag-string properties use the string overload with the server name.
            Assert.AreEqual("web01", ValueOf("appserv;web tier"));
            Assert.AreEqual("web02", ValueOf("web tier"));

            var allServers = (string[])((VariableValue)ValueOf("AllServers")!).Value;
            CollectionAssert.AreEqual(new[] { "web01", "web02" }, allServers);

            var envServers = (VariableValueServers[])((VariableValue)ValueOf("EnvironmentServers")!).Value;
            Assert.AreEqual(2, envServers.Length);
            Assert.AreEqual("web01", envServers[0].Name);
            Assert.AreEqual("Win2022", envServers[0].OsName);
            Assert.AreEqual("appserv;web tier", envServers[0].ApplicationServerName);
            Assert.AreEqual(1, envServers[0].Services.Length);
            Assert.AreEqual("EndurSvc", envServers[0].Services[0].Name);
            Assert.AreEqual("Endur Service", envServers[0].Services[0].DisplayName);
            Assert.AreEqual(@"DOM\svc", envServers[0].Services[0].AccountName);
            Assert.AreEqual("WindowsService", envServers[0].Services[0].ServiceType);
            Assert.AreEqual("web02", envServers[1].Name);
            Assert.AreEqual("Win2022", envServers[1].OsName);
            Assert.AreEqual("web tier", envServers[1].ApplicationServerName);
            Assert.AreEqual(0, envServers[1].Services.Length);

            Assert.AreEqual(@"\\share\dv07", ValueOf("EndurFileShare"));
            Assert.AreEqual("cfg/path", ValueOf("EndurConfigurationFile"));
            Assert.AreEqual("END_DB_DV07", ValueOf("EndurDatabaseName"));
            Assert.AreEqual("sql01", ValueOf("EndurDatabaseServer"));
            // Short name derives from the Endur DB name, END_DB_ prefix stripped.
            Assert.AreEqual("DV07", ValueOf("EnvironmentShortName"));
            Assert.AreEqual("REP_DB", ValueOf("ReportingDatabaseName"));
            Assert.AreEqual("sql02", ValueOf("ReportingDatabaseServer"));
            Assert.AreEqual("sql02", ValueOf("SsisPackageServer"));
            Assert.AreEqual("EXT_DB", ValueOf("ExternalDatabaseName"));
            Assert.AreEqual("sql04", ValueOf("ExternalDatabaseServer"));

            // Single database per type → scalar string inside VariableValue.
            Assert.AreEqual("sql01", ((VariableValue)ValueOf("DbServer_Endur")!).Value);
            Assert.AreEqual("END_DB_DV07", ((VariableValue)ValueOf("DbName_Endur")!).Value);
            Assert.AreEqual("sql02", ((VariableValue)ValueOf("DbServer_Endur_Reporting")!).Value);
            Assert.AreEqual("REP_DB", ((VariableValue)ValueOf("DbName_Endur_Reporting")!).Value);
            // Two databases of one type → string[] variants.
            CollectionAssert.AreEqual(new[] { "sql03", "sql03" },
                (string[])((VariableValue)ValueOf("DbServer_Audit")!).Value);
            CollectionAssert.AreEqual(new[] { "AUD1", "AUD2" },
                (string[])((VariableValue)ValueOf("DbName_Audit")!).Value);
            Assert.AreEqual("sql04", ((VariableValue)ValueOf("DbServer_Endur_External")!).Value);
            Assert.AreEqual("EXT_DB", ((VariableValue)ValueOf("DbName_Endur_External")!).Value);

            // Per-tag quirks: one server → scalar via string overload; two → string[] in VariableValue;
            // tag "web tier" becomes ServerNames_web_tier (space → underscore).
            Assert.AreEqual("web01", ValueOf("ServerNames_appserv"));
            var webTierServers = (string[])((VariableValue)ValueOf("ServerNames_web_tier")!).Value;
            CollectionAssert.AreEqual(new[] { "web01", "web02" }, webTierServers);

            var dbPerms = (VariableValueDbPerm[])((VariableValue)ValueOf("DatabasePermissions")!).Value;
            Assert.AreEqual(5, dbPerms.Length);
            Assert.AreEqual("END_DB_DV07", dbPerms[0].Database.Name);
            Assert.AreEqual("Endur", dbPerms[0].Database.Type);
            Assert.AreEqual(1, dbPerms[0].Users.Length);
            Assert.AreEqual(@"DOM\alice", dbPerms[0].Users[0].User);
            CollectionAssert.AreEqual(new[] { "db_owner", "db_datareader" }, dbPerms[0].Users[0].Roles);
            Assert.AreEqual(0, dbPerms[1].Users.Length);

            var ownerEmails = (string[])((VariableValue)ValueOf("EnvOwnerEmails")!).Value;
            CollectionAssert.AreEqual(new[] { "owner@example.com" }, ownerEmails);
        }

        [TestMethod]
        public void SetPropertyValues_MinimalEnvironment_EmitsFrozenCallSet()
        {
            var environment = BuildEnvironment(ownerEmails: null);

            _servers.GetServersForEnvId(42).Returns(Array.Empty<ServerApiModel>());
            _databases.GetDatabasesForEnvironmentName("IAR DV 07")
                .Returns(Array.Empty<DatabaseApiModel>());
            _databases.GetDatabaseByType(environment, "Endur").Returns((DatabaseApiModel?)null);
            _properties.GetConfigurationFilePath(environment).Returns("cfg/path");

            CreateResolver().SetPropertyValues(CreateRecordingVariableResolver(), environment);

            var expectedNames = new[]
            {
                "AllServers",
                "EnvironmentServers",
                "EndurFileShare",
                "EndurConfigurationFile",
                "EnvironmentShortName",
                "DatabasePermissions"
            };
            CollectionAssert.AreEqual(expectedNames, _calls.Select(c => c.Name).ToArray(),
                $"Emitted call sequence changed. Actual: [{string.Join(", ", _calls.Select(c => c.Name))}]");

            Assert.AreEqual(0, ((string[])((VariableValue)ValueOf("AllServers")!).Value).Length);
            Assert.AreEqual(0, ((VariableValueServers[])((VariableValue)ValueOf("EnvironmentServers")!).Value).Length);
            // No Endur DB → short name falls back to the environment name derivation.
            Assert.AreEqual("DV07", ValueOf("EnvironmentShortName"));
            Assert.AreEqual(0, ((VariableValueDbPerm[])((VariableValue)ValueOf("DatabasePermissions")!).Value).Length);
        }

        private object? ValueOf(string name)
        {
            var matches = _calls.Where(c => c.Name == name).ToArray();
            Assert.AreEqual(1, matches.Length, $"Expected exactly one emission of '{name}'");
            return matches[0].Value;
        }
    }
}
