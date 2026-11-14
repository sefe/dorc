using Dorc.Api.Tests.Mocks;
using Dorc.ApiModel;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using NSubstitute;
using System.Security.Principal;
using Environment = Dorc.PersistentData.Model.Environment;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Security.Claims;

namespace Dorc.Api.Tests
{
    [TestClass]
    public class EnvironmentsPersistentSourceTests
    {
        private IDeploymentContextFactory _contextFactory;
        private ISecurityObjectFilter _objectFilter;
        private IRolePrivilegesChecker _rolePrivilegesChecker;
        private IPropertyValuesPersistentSource _propertyValuesPersistentSource;
        private ILogger<EnvironmentsPersistentSource> _logger;
        private IClaimsPrincipalReader _claimsPrincipalReader;
        private IAccessControlPersistentSource _accessControlPersistentSource;
        private EnvironmentsPersistentSource _environmentsPersistentSource;
        private IPrincipal _user;
        private IDeploymentContext _context;

        [TestInitialize]
        public void Setup()
        {
            _contextFactory = Substitute.For<IDeploymentContextFactory>();
            _objectFilter = Substitute.For<ISecurityObjectFilter>();
            _rolePrivilegesChecker = Substitute.For<IRolePrivilegesChecker>();
            _propertyValuesPersistentSource = Substitute.For<IPropertyValuesPersistentSource>();
            _logger = Substitute.For<ILogger<EnvironmentsPersistentSource>>();
            _claimsPrincipalReader = Substitute.For<IClaimsPrincipalReader>();
            _accessControlPersistentSource = Substitute.For<IAccessControlPersistentSource>();
            _user = Substitute.For<IPrincipal>();
            _context = Substitute.For<IDeploymentContext>();

            _environmentsPersistentSource = new EnvironmentsPersistentSource(
                _contextFactory,
                _objectFilter,
                _rolePrivilegesChecker,
                _propertyValuesPersistentSource,
                _logger,
                _claimsPrincipalReader,
                _accessControlPersistentSource
            );

            _contextFactory.GetContext().Returns(_context);
            _claimsPrincipalReader.GetUserFullDomainName(_user).Returns("testuser@domain.com");
            _rolePrivilegesChecker.IsAdmin(_user).Returns(true);
        }

        [TestMethod]
        [DataRow(false, false, false)]
        [DataRow(true, false, false)]
        [DataRow(false, true, false)]
        [DataRow(false, false, true)]
        [DataRow(true, true, true)]
        public void CloneEnvironment_CopiesNewComponentMappingsOnlyWhenExplicitlyRequested(
            bool containers, bool cloudResources, bool apiRegistrations)
        {
            var original = new Environment
            {
                Id = 10, Name = "SOURCE", ObjectId = Guid.NewGuid(),
                Containers = new List<Container> { new() { Id = 1, Name = "container" } },
                CloudResources = new List<CloudResource> { new() { Id = 2, Name = "subscription" } },
                ApiRegistrations = new List<ApiRegistration> { new() { Id = 3, Name = "api" } }
            };
            var environments = new List<Environment> { original };
            var environmentSet = DbContextMock.GetQueryableMockDbSet(environments);
            var noDuplicateEnvironment = DbContextMock.GetQueryableMockDbSet(new List<Environment>());
            var accessSet = DbContextMock.GetQueryableMockDbSet(new List<AccessControl>());
            var historySet = DbContextMock.GetQueryableMockDbSet(new List<EnvironmentHistory>());
            // The duplicate-name query uses SQL COLLATE, which cannot execute in LINQ-to-Objects.
            _context.Environments.Returns(environmentSet, noDuplicateEnvironment, environmentSet);
            _context.AccessControls.Returns(accessSet);
            _context.EnvironmentHistories.Returns(historySet);
            using var dbContext = new DbContext(new DbContextOptions<DbContext>());
            var database = Substitute.For<DatabaseFacade>(dbContext);
            database.CreateExecutionStrategy().Returns(new DirectExecutionStrategy(dbContext));
            database.BeginTransaction().Returns(Substitute.For<IDbContextTransaction>());
            _context.Database.Returns(database);

            _environmentsPersistentSource.CloneEnvironment(new CloneEnvironmentRequest
            {
                SourceEnvironmentId = original.Id, NewEnvironmentName = "CLONE",
                CopyPropertyValues = false,
                CopyContainerMappings = containers,
                CopyCloudResourceMappings = cloudResources,
                CopyApiRegistrationMappings = apiRegistrations
            }, new ClaimsPrincipal());

            var clone = environments.Single(e => e.Name == "CLONE");
            Assert.AreEqual(containers ? 1 : 0, clone.Containers.Count);
            Assert.AreEqual(cloudResources ? 1 : 0, clone.CloudResources.Count);
            Assert.AreEqual(apiRegistrations ? 1 : 0, clone.ApiRegistrations.Count);
            if (containers) Assert.AreSame(original.Containers.Single(), clone.Containers.Single());
            if (cloudResources) Assert.AreSame(original.CloudResources.Single(), clone.CloudResources.Single());
            if (apiRegistrations) Assert.AreSame(original.ApiRegistrations.Single(), clone.ApiRegistrations.Single());
            Assert.AreEqual(1, original.CloudResources.Count);
        }

        private sealed class DirectExecutionStrategy(DbContext context) : IExecutionStrategy
        {
            public bool RetriesOnFailure => false;

            public TResult Execute<TState, TResult>(TState state,
                Func<DbContext, TState, TResult> operation,
                Func<DbContext, TState, ExecutionResult<TResult>>? verifySucceeded) =>
                operation(context, state);

            public Task<TResult> ExecuteAsync<TState, TResult>(TState state,
                Func<DbContext, TState, CancellationToken, Task<TResult>> operation,
                Func<DbContext, TState, CancellationToken, Task<ExecutionResult<TResult>>>? verifySucceeded,
                CancellationToken cancellationToken = default) =>
                operation(context, state, cancellationToken);
        }

        private class TestEnvironment
        {
            public Environment Environment { get; set; }
            public List<AccessControl> AccessControls { get; set; }
            public List<Environment> Environments { get; set; }
            public DbSet<Environment> EnvironmentsDbSet { get; set; }
            public DbSet<AccessControl> AccessControlsDbSet { get; set; }
            public DbSet<EnvironmentHistory> EnvironmentHistoriesDbSet { get; set; }
        }

        private TestEnvironment SetupTestEnvironment(int envId, string oldOwnerSid, AccessLevel oldOwnerAccessLevel, string newOwnerSid = null, AccessLevel? newOwnerAccessLevel = null)
        {
            var environment = new Environment { Id = envId, ObjectId = Guid.NewGuid() };
            var accessControls = new List<AccessControl>();
            
            // Setup old owner access
            var oldOwnerAccess = new AccessControl 
            { 
                ObjectId = environment.ObjectId,
                Pid = oldOwnerSid,
                Name = "Old Owner",
                Allow = (int)oldOwnerAccessLevel
            };
            environment.AccessControls.Add(oldOwnerAccess);
            accessControls.Add(oldOwnerAccess);

            // Setup new owner access if provided
            if (newOwnerSid != null && newOwnerAccessLevel.HasValue)
            {
                var newOwnerAccess = new AccessControl 
                { 
                    ObjectId = environment.ObjectId,
                    Pid = newOwnerSid,
                    Name = "New Owner",
                    Allow = (int)newOwnerAccessLevel.Value
                };
                environment.AccessControls.Add(newOwnerAccess);
                accessControls.Add(newOwnerAccess);
            }

            var environments = new List<Environment> { environment };
            var environmentsDbSet = DbContextMock.GetQueryableMockDbSet(environments);
            var accessControlsDbSet = DbContextMock.GetQueryableMockDbSet(accessControls);
            var environmentHistoriesDbSet = DbContextMock.GetQueryableMockDbSet(new List<EnvironmentHistory>());

            _context.Environments.Returns(environmentsDbSet);
            _context.AccessControls.Returns(accessControlsDbSet);
            _context.EnvironmentHistories.Returns(environmentHistoriesDbSet);

            // Mock the Include functionality
            _accessControlPersistentSource.GetAccessControls(environment.ObjectId).Returns(accessControls.Select(ac => new AccessControlApiModel 
            { 
                Sid = ac.Sid,
                Pid = ac.Pid,
                Name = ac.Name,
                Allow = ac.Allow,
                Deny = ac.Deny
            }));

            return new TestEnvironment
            {
                Environment = environment,
                AccessControls = accessControls,
                Environments = environments,
                EnvironmentsDbSet = environmentsDbSet,
                AccessControlsDbSet = accessControlsDbSet,
                EnvironmentHistoriesDbSet = environmentHistoriesDbSet
            };
        }

        private void AssertOwnerChange(TestEnvironment testEnv, string oldOwnerSid, string newOwnerSid, AccessLevel? expectedOldOwnerAccess = null, AccessLevel? expectedNewOwnerAccess = null)
        {
            // Verify old owner's access
            var oldOwnerAccessControl = testEnv.AccessControls.FirstOrDefault(ac => ac.Pid == oldOwnerSid);
            if (expectedOldOwnerAccess.HasValue)
            {
                Assert.IsNotNull(oldOwnerAccessControl);
                Assert.AreEqual((int)expectedOldOwnerAccess.Value, oldOwnerAccessControl.Allow);
            }
            else
            {
                Assert.IsNull(oldOwnerAccessControl);
            }

            // Verify new owner's access
            var newOwnerAccessControl = testEnv.AccessControls.FirstOrDefault(ac => ac.Pid == newOwnerSid);
            if (expectedNewOwnerAccess.HasValue)
            {
                Assert.IsNotNull(newOwnerAccessControl);
                Assert.AreEqual((int)expectedNewOwnerAccess.Value, newOwnerAccessControl.Allow);
            }
            else
            {
                Assert.IsNull(newOwnerAccessControl);
            }

            // Verify owner through GetEnvironmentOwnerId
            var ownerId = _environmentsPersistentSource.GetEnvironmentOwnerId(testEnv.Environment.Id);
            Assert.AreEqual(newOwnerSid, ownerId);
        }

        [TestMethod]
        public void SetEnvironmentOwner_WithExistingOwnerHavingWriteAccess_PreservesWriteAccess()
        {
            // Arrange
            var envId = 1;
            var oldOwnerSid = "old-owner-sid";
            var newOwnerSid = "new-owner-sid";
            var newOwner = new UserElementApiModel { Username = "newowner", DisplayName = "New Owner", Pid = newOwnerSid };
            
            var testEnv = SetupTestEnvironment(envId, oldOwnerSid, AccessLevel.Owner | AccessLevel.Write);

            // Act
            var result = _environmentsPersistentSource.SetEnvironmentOwner(_user, envId, newOwner);

            // Assert
            Assert.IsTrue(result);
            AssertOwnerChange(testEnv, oldOwnerSid, newOwnerSid, AccessLevel.Write, AccessLevel.Owner);
        }

        [TestMethod]
        public void SetEnvironmentOwner_WithExistingOwnerHavingOnlyOwnerAccess_RemovesAccessControl()
        {
            // Arrange
            var envId = 1;
            var oldOwnerSid = "old-owner-sid";
            var newOwnerSid = "new-owner-sid";
            var newOwner = new UserElementApiModel { Username = "newowner", DisplayName = "New Owner", Pid = newOwnerSid };
            
            var testEnv = SetupTestEnvironment(envId, oldOwnerSid, AccessLevel.Owner);

            // Act
            var result = _environmentsPersistentSource.SetEnvironmentOwner(_user, envId, newOwner);

            // Assert
            Assert.IsTrue(result);
            AssertOwnerChange(testEnv, oldOwnerSid, newOwnerSid, null, AccessLevel.Owner);
        }

        [TestMethod]
        public void SetEnvironmentOwner_WithNewOwnerHavingExistingAccess_PreservesAndAddsOwnerAccess()
        {
            // Arrange
            var envId = 1;
            var oldOwnerSid = "old-owner-sid";
            var newOwnerSid = "new-owner-sid";
            var newOwner = new UserElementApiModel { Username = "newowner", DisplayName = "New Owner", Pid = newOwnerSid };
            
            var testEnv = SetupTestEnvironment(envId, oldOwnerSid, AccessLevel.Owner, newOwnerSid, AccessLevel.Write);

            // Act
            var result = _environmentsPersistentSource.SetEnvironmentOwner(_user, envId, newOwner);

            // Assert
            Assert.IsTrue(result);
            AssertOwnerChange(testEnv, oldOwnerSid, newOwnerSid, null, AccessLevel.Owner | AccessLevel.Write);
        }

        [TestMethod]
        public void GetEnvironmentComponentStatuses_AfterComponentRename_ReturnsPinnedComponentName()
        {
            var env = new Environment { Id = 10, Name = "UT02", ObjectId = Guid.NewGuid() };
            var liveComponent = new Component
            {
                Id = 42,
                Name = "RenamedJobName",
                ComponentType = ComponentType.PowerShell,
                IsEnabled = true
            };
            var completedAt = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
            var request = new DeploymentRequest
            {
                Id = 77,
                Project = "TestProject",
                Environment = "UT02",
                BuildNumber = "1.0.0",
                RequestDetails = "<DeploymentRequestDetail><BuildDetail><BuildNumber>1.0.0</BuildNumber></BuildDetail></DeploymentRequestDetail>",
                CompletedTime = completedAt,
                Status = "Completed"
            };
            var envStatus = new EnvironmentComponentStatus
            {
                Id = 1,
                Environment = env,
                Component = liveComponent,
                DeploymentRequest = request,
                Status = "Complete",
                UpdateDate = completedAt
            };
            var pinnedModel = new ComponentApiModel
            {
                ComponentId = 42,
                ComponentName = "OriginalJobName",
                ComponentType = ComponentType.PowerShell,
                IsEnabled = true
            };
            var definition = new DeploymentRequestDefinition
            {
                DeploymentRequestId = 77,
                ProjectId = 5,
                ComponentId = 42,
                ComponentName = "OriginalJobName",
                DefinitionRevision = new string('a', 64),
                DefinitionJson = System.Text.Json.JsonSerializer.Serialize(pinnedModel),
                ProjectJson = "{}"
            };
            var resultRow = new DeploymentResult
            {
                Id = 500,
                Component = liveComponent,
                DeploymentRequest = request,
                Status = "Complete"
            };

            using var dbContext = new DbContext(
                new DbContextOptionsBuilder<DbContext>().UseSqlServer("Server=.;Database=Dummy;").Options);
            var database = Substitute.For<DatabaseFacade>(dbContext);
            _context.Database.Returns(database);
            var envStatusesSet1 = DbContextMock.GetQueryableMockDbSet(new List<EnvironmentComponentStatus> { envStatus });
            var envStatusesSet2 = DbContextMock.GetQueryableMockDbSet(new List<EnvironmentComponentStatus> { envStatus });
            var deploymentResultsSet = DbContextMock.GetQueryableMockDbSet(new List<DeploymentResult> { resultRow });
            var definitionsSet1 = DbContextMock.GetQueryableMockDbSet(new List<DeploymentRequestDefinition> { definition });
            var definitionsSet2 = DbContextMock.GetQueryableMockDbSet(new List<DeploymentRequestDefinition> { definition });
            var tfReceiptsSet = DbContextMock.GetQueryableMockDbSet(new List<TerraformDeploymentReceipt>());
            var psReceiptsSet = DbContextMock.GetQueryableMockDbSet(new List<PowerShellExecutionReceipt>());
            var requestsSet = DbContextMock.GetQueryableMockDbSet(new List<DeploymentRequest> { request });

            _context.EnvironmentComponentStatuses.Returns(envStatusesSet1, envStatusesSet2);
            _context.DeploymentResults.Returns(deploymentResultsSet);
            _context.DeploymentRequestDefinitions.Returns(definitionsSet1, definitionsSet2);
            _context.TerraformDeploymentReceipts.Returns(tfReceiptsSet);
            _context.PowerShellExecutionReceipts.Returns(psReceiptsSet);
            _context.DeploymentRequests.Returns(requestsSet);

            var builds = _environmentsPersistentSource
                .GetEnvironmentComponentStatuses("UT02", completedAt.UtcDateTime.AddMinutes(5))
                .ToList();
            Assert.HasCount(1, builds);
            Assert.AreEqual("OriginalJobName", builds[0].ComponentName);
            Assert.AreEqual(new string('a', 64), builds[0].DefinitionRevision);

            var models = _environmentsPersistentSource
                .GetEnvironmentComponentStatuses(10)
                .ToList();
            Assert.HasCount(1, models);
            Assert.AreEqual("OriginalJobName", models[0].Component);
        }

        [TestMethod]
        public void RequestsPersistentSource_AfterComponentRename_PreservesPinnedNameAndEnabledAcrossRestartAndAttempts()
        {
            var liveComponent = new Component
            {
                Id = 42,
                Name = "RenamedJobName",
                ComponentType = ComponentType.PowerShell,
                IsEnabled = false
            };
            var request = new DeploymentRequest
            {
                Id = 77,
                Project = "TestProject",
                Environment = "UT02",
                BuildNumber = "1.0.0",
                Status = "Failed",
                UserName = "tester"
            };
            var pinnedModel = new ComponentApiModel
            {
                ComponentId = 42,
                ComponentName = "OriginalJobName",
                ComponentType = ComponentType.PowerShell,
                IsEnabled = true
            };
            var definition = new DeploymentRequestDefinition
            {
                DeploymentRequestId = 77,
                ProjectId = 5,
                ComponentId = 42,
                ComponentName = "OriginalJobName",
                DefinitionRevision = new string('b', 64),
                DefinitionJson = System.Text.Json.JsonSerializer.Serialize(pinnedModel),
                ProjectJson = "{}"
            };
            var existingResult = new DeploymentResult
            {
                Id = 500,
                Component = liveComponent,
                DeploymentRequest = request,
                Status = "Failed"
            };
            var results = new List<DeploymentResult> { existingResult };
            var requestAttempts = new List<DeploymentRequestAttempt>();
            var resultAttempts = new List<DeploymentResultAttempt>();

            _context.Components.Returns(_ => DbContextMock.GetQueryableMockDbSet(new List<Component> { liveComponent }));
            _context.DeploymentRequests.Returns(_ => DbContextMock.GetQueryableMockDbSet(new List<DeploymentRequest> { request }));
            _context.DeploymentResults.Returns(_ => DbContextMock.GetQueryableMockDbSet(results));
            _context.DeploymentRequestDefinitions.Returns(_ => DbContextMock.GetQueryableMockDbSet(new List<DeploymentRequestDefinition> { definition }));
            _context.DeploymentRequestAttempts.Returns(_ => DbContextMock.GetQueryableMockDbSet(requestAttempts));
            _context.DeploymentResultAttempts.Returns(_ => DbContextMock.GetQueryableMockDbSet(resultAttempts));
            _context.TerraformDeploymentReceipts.Returns(_ => DbContextMock.GetQueryableMockDbSet(new List<TerraformDeploymentReceipt>()));
            _context.TerraformPlanApprovals.Returns(_ => DbContextMock.GetQueryableMockDbSet(new List<TerraformPlanApproval>()));
            _context.PowerShellExecutionReceipts.Returns(_ => DbContextMock.GetQueryableMockDbSet(new List<PowerShellExecutionReceipt>()));

            var requestsSource = new RequestsPersistentSource(_contextFactory, _claimsPrincipalReader);

            // 1. ArchiveCurrentAttempt stores the pinned ComponentName rather than the live renamed Component.Name
            requestsSource.ArchiveCurrentAttempt(77);
            Assert.HasCount(1, resultAttempts);
            Assert.AreEqual("OriginalJobName", resultAttempts[0].ComponentName);

            // 2. Even if an older attempt row in DB had the live name stored, GetArchivedDeploymentResult returns the pinned name and SHA
            resultAttempts[0].ComponentName = "RenamedJobName";
            resultAttempts[0].DeploymentRequestAttempt = requestAttempts[0];
            var archived = requestsSource.GetArchivedDeploymentResult(500);
            Assert.IsNotNull(archived);
            Assert.AreEqual("OriginalJobName", archived.ComponentName);
            Assert.AreEqual(new string('b', 64), archived.DefinitionRevision);

            // 3. CreateDeploymentResult on restart uses pinned IsEnabled (true) and pinned ComponentName ("OriginalJobName")
            results.Clear();
            var newResult = requestsSource.CreateDeploymentResult(42, 77);
            Assert.AreEqual("OriginalJobName", newResult.ComponentName);
            Assert.AreEqual(DeploymentResultStatus.Pending.ToString(), newResult.Status);
            Assert.AreEqual(new string('b', 64), newResult.DefinitionRevision);
        }

        [TestMethod]
        public void IsEnvironmentOwner_WhenUserBelongsToGroupWithOwnerAccess_ReturnsTrue()
        {
            var envObjectId = Guid.NewGuid();
            var env = new Environment { Id = 1, Name = "TeamEnv", ObjectId = envObjectId };
            var envSet = DbContextMock.GetQueryableMockDbSet(new List<Environment> { env });
            _context.Environments.Returns(envSet);

            _accessControlPersistentSource.GetAccessControls(envObjectId).Returns(new List<AccessControlApiModel>
            {
                new AccessControlApiModel { Id = 1, Name = "ADTeamGroup", Pid = "S-1-5-21-TEAM-GROUP", Allow = (int)AccessLevel.Owner, Deny = 0 }
            });

            var user = new ClaimsPrincipal(new ClaimsIdentity());
            _claimsPrincipalReader.GetUserId(user).Returns("user-sid-123");
            _claimsPrincipalReader.GetUserLogin(user).Returns("jdoe");
            _claimsPrincipalReader.GetSidsForUser(user).Returns(new List<string> { "user-sid-123", "S-1-5-21-TEAM-GROUP" });

            Assert.IsTrue(_environmentsPersistentSource.IsEnvironmentOwner("TeamEnv", user));
        }

        [TestMethod]
        public void IsEnvironmentOwner_WhenMatchingAccessControlHasDeny_ReturnsFalse()
        {
            var envObjectId = Guid.NewGuid();
            var env = new Environment { Id = 1, Name = "TeamEnv", ObjectId = envObjectId };
            var envSet = DbContextMock.GetQueryableMockDbSet(new List<Environment> { env });
            _context.Environments.Returns(envSet);

            _accessControlPersistentSource.GetAccessControls(envObjectId).Returns(new List<AccessControlApiModel>
            {
                new AccessControlApiModel { Id = 1, Name = "ADTeamGroup", Pid = "S-1-5-21-TEAM-GROUP", Allow = (int)AccessLevel.Owner, Deny = 0 },
                new AccessControlApiModel { Id = 2, Name = "DeniedUser", Pid = "user-sid-123", Allow = 0, Deny = (int)AccessLevel.Owner }
            });

            var user = new ClaimsPrincipal(new ClaimsIdentity());
            _claimsPrincipalReader.GetUserId(user).Returns("user-sid-123");
            _claimsPrincipalReader.GetUserLogin(user).Returns("jdoe");
            _claimsPrincipalReader.GetSidsForUser(user).Returns(new List<string> { "user-sid-123", "S-1-5-21-TEAM-GROUP" });

            Assert.IsFalse(_environmentsPersistentSource.IsEnvironmentOwner("TeamEnv", user));
        }

        [TestMethod]
        public void AccessibleEnvironmentsAccessLevel_GetEnvironment_And_GetPossibleChildren_RespectExplicitReadSecretsAndDeny()
        {
            var envAllowedId = Guid.NewGuid();
            var envDeniedId = Guid.NewGuid();
            var envAllowed = new Environment
            {
                Id = 1,
                Name = "AllowedEnv",
                ObjectId = envAllowedId,
                AccessControls = new List<AccessControl>
                {
                    new AccessControl { Id = 1, ObjectId = envAllowedId, Sid = "user-sid", Allow = (int)(AccessLevel.Owner | AccessLevel.Write), Deny = 0 }
                }
            };
            var envDenied = new Environment
            {
                Id = 2,
                Name = "DeniedEnv",
                ObjectId = envDeniedId,
                AccessControls = new List<AccessControl>
                {
                    new AccessControl { Id = 2, ObjectId = envDeniedId, Sid = "user-sid", Allow = (int)(AccessLevel.Owner | AccessLevel.Write | AccessLevel.ReadSecrets), Deny = 1 }
                }
            };
            var project = new Project
            {
                Id = 10,
                Name = "ProjA",
                Environments = new List<Environment> { envAllowed, envDenied }
            };
            envAllowed.Projects.Add(project);
            envDenied.Projects.Add(project);

            var allEnvs = new List<Environment> { envAllowed, envDenied };
            var allAcs = envAllowed.AccessControls.Concat(envDenied.AccessControls).ToList();

            _context.Projects.Returns(_ => DbContextMock.GetQueryableMockDbSet(new List<Project> { project }));
            _context.Environments.Returns(_ => DbContextMock.GetQueryableMockDbSet(allEnvs));
            _context.AccessControls.Returns(_ => DbContextMock.GetQueryableMockDbSet(allAcs));
            _context.GetFullEnvironmentChain(1).Returns(new List<EnvironmentChainItemDto> { new EnvironmentChainItemDto { Id = 1, Name = "AllowedEnv" } });
            _context.GetFullEnvironmentChain(2).Returns(new List<EnvironmentChainItemDto> { new EnvironmentChainItemDto { Id = 2, Name = "DeniedEnv" } });

            var user = new ClaimsPrincipal(new ClaimsIdentity("TestAuth"));
            _claimsPrincipalReader.GetUserLogin(user).Returns("user1");
            _claimsPrincipalReader.GetSidsForUser(user).Returns(new List<string> { "user-sid" });

            // 1. AccessibleEnvironmentsAccessLevel: Admin does NOT bypass ReadSecrets, and Owner/Write on AllowedEnv does NOT grant ReadSecrets
            _rolePrivilegesChecker.IsAdmin(user).Returns(true);
            var secretEnvs = _environmentsPersistentSource.AccessibleEnvironmentsAccessLevel(_context, "ProjA", user, AccessLevel.ReadSecrets).ToList();
            Assert.AreEqual(0, secretEnvs.Count);
            var adminWriteEnvs = _environmentsPersistentSource.AccessibleEnvironmentsAccessLevel(_context, "ProjA", user, AccessLevel.Write).ToList();
            Assert.AreEqual(2, adminWriteEnvs.Count);

            // Grant explicit ReadSecrets on AllowedEnv -> now returned for ReadSecrets, while DeniedEnv is still blocked by Deny != 0
            envAllowed.AccessControls.First().Allow |= (int)AccessLevel.ReadSecrets;
            var secretEnvsAfterGrant = _environmentsPersistentSource.AccessibleEnvironmentsAccessLevel(_context, "ProjA", user, AccessLevel.ReadSecrets).ToList();
            Assert.AreEqual(1, secretEnvsAfterGrant.Count);
            Assert.AreEqual("AllowedEnv", secretEnvsAfterGrant[0].Environment.Name);

            // Non-admin Write access check returns AllowedEnv and excludes DeniedEnv
            _rolePrivilegesChecker.IsAdmin(user).Returns(false);
            var writeEnvs = _environmentsPersistentSource.AccessibleEnvironmentsAccessLevel(_context, "ProjA", user, AccessLevel.Write).ToList();
            Assert.AreEqual(1, writeEnvs.Count);
            Assert.AreEqual("AllowedEnv", writeEnvs[0].Environment.Name);

            var noneEnvs = _environmentsPersistentSource.AccessibleEnvironmentsAccessLevel(_context, "ProjA", user, AccessLevel.None).ToList();
            Assert.AreEqual(2, noneEnvs.Count);

            // 2. GetEnvironments(user) (private AccessibleEnvironmentsAccessLevel overload) sets UserEditable/IsOwner false when Deny != 0
            var allUserEnvs = _environmentsPersistentSource.GetEnvironments(user).ToList();
            var allowedModel = allUserEnvs.Single(e => e.EnvironmentName == "AllowedEnv");
            var deniedModel = allUserEnvs.Single(e => e.EnvironmentName == "DeniedEnv");
            Assert.IsTrue(allowedModel.UserEditable);
            Assert.IsTrue(allowedModel.IsOwner);
            Assert.IsFalse(deniedModel.UserEditable);
            Assert.IsFalse(deniedModel.IsOwner);

            // 3. GetEnvironment(string, user) & GetEnvironment(int, user)
            var singleAllowed = _environmentsPersistentSource.GetEnvironment("AllowedEnv", user);
            var singleDenied = _environmentsPersistentSource.GetEnvironment("DeniedEnv", user);
            Assert.IsTrue(singleAllowed.UserEditable);
            Assert.IsFalse(singleDenied.UserEditable);

            _rolePrivilegesChecker.IsAdmin(user).Returns(true);
            var singleAdmin = _environmentsPersistentSource.GetEnvironment("DeniedEnv", user);
            Assert.IsTrue(singleAdmin.UserEditable);
            Assert.IsFalse(singleAdmin.IsOwner);

            var byId = _environmentsPersistentSource.GetEnvironment(1, user);
            Assert.IsNotNull(byId);
            Assert.IsTrue(byId.UserEditable);

            _rolePrivilegesChecker.IsAdmin(user).Returns(false);
            _objectFilter.HasPrivilege(envAllowed, user, AccessLevel.Write | AccessLevel.Owner).Returns(true);
            Assert.IsTrue(_environmentsPersistentSource.GetEnvironment(1, user)!.UserEditable);

            _objectFilter.HasPrivilege(envAllowed, user, AccessLevel.Write | AccessLevel.Owner).Returns(false);
            _accessControlPersistentSource.GetAccessControls(envAllowedId).Returns(new List<AccessControlApiModel>
            {
                new AccessControlApiModel { Id = 1, Sid = "user-sid", Allow = (int)AccessLevel.Owner, Deny = 0 }
            });
            Assert.IsTrue(_environmentsPersistentSource.GetEnvironment(1, user)!.UserEditable);

            _accessControlPersistentSource.GetAccessControls(envAllowedId).Returns(new List<AccessControlApiModel>());
            Assert.IsFalse(_environmentsPersistentSource.GetEnvironment(1, user)!.UserEditable);

            // 4. GetPossibleEnvironmentChildren excludes DeniedEnv because of Deny != 0
            _rolePrivilegesChecker.IsAdmin(user).Returns(false);
            var possibleChildren = _environmentsPersistentSource.GetPossibleEnvironmentChildren(2, user).ToList();
            Assert.AreEqual(1, possibleChildren.Count);
            Assert.AreEqual("AllowedEnv", possibleChildren[0].EnvironmentName);

            var childrenOfAllowed = _environmentsPersistentSource.GetPossibleEnvironmentChildren(1, user).ToList();
            Assert.AreEqual(0, childrenOfAllowed.Count);
        }

        [TestMethod]
        public void DeleteEnvironment_And_CloneEnvironment_EnforceSecurityCovenants()
        {
            var prodEnv = new Environment
            {
                Id = 10,
                Name = "PROD",
                ObjectId = Guid.NewGuid(),
                IsProd = true,
                Servers = new List<Server> { new Server { Id = 1, Name = "srv1" } }
            };
            var devEnv = new Environment { Id = 20, Name = "DEV", ObjectId = Guid.NewGuid(), IsProd = false };
            var environments = new List<Environment> { prodEnv, devEnv };
            var filter = new PropertyFilter { Id = 1, Name = "environment" };
            var plainProp = new Property { Id = 1, Name = "PlainProp", Secure = false };
            var secretProp = new Property { Id = 2, Name = "SecretProp", Secure = true };
            var pvfList = new List<PropertyValueFilter>
            {
                new PropertyValueFilter
                {
                    Id = 1,
                    Value = "DEV",
                    PropertyFilter = filter,
                    PropertyValue = new PropertyValue { Id = 1, Value = "plain-val", Property = plainProp }
                },
                new PropertyValueFilter
                {
                    Id = 2,
                    Value = "DEV",
                    PropertyFilter = filter,
                    PropertyValue = new PropertyValue { Id = 2, Value = "enc-val", Property = secretProp }
                }
            };
            var pvList = new List<PropertyValue>();

            var envDbSet = DbContextMock.GetQueryableMockDbSet(environments);
            var acDbSet = DbContextMock.GetQueryableMockDbSet(new List<AccessControl>());
            var histDbSet = DbContextMock.GetQueryableMockDbSet(new List<EnvironmentHistory>());
            var propFilterSet = DbContextMock.GetQueryableMockDbSet(new List<PropertyFilter> { filter });
            propFilterSet.Find(1).Returns(filter);
            var pvfSet = DbContextMock.GetQueryableMockDbSet(pvfList);
            var pvSet = DbContextMock.GetQueryableMockDbSet(pvList);
            var auditsSet = DbContextMock.GetQueryableMockDbSet(new List<Audit>());

            _context.Environments.Returns(envDbSet);
            _context.AccessControls.Returns(acDbSet);
            _context.EnvironmentHistories.Returns(histDbSet);
            _context.PropertyFilters.Returns(propFilterSet);
            _context.PropertyValueFilters.Returns(pvfSet);
            _context.PropertyValues.Returns(pvSet);
            _context.Audits.Returns(auditsSet);

            using var dbContext = new DbContext(
                new DbContextOptionsBuilder<DbContext>().UseSqlServer("Server=.;Database=Dummy;").Options);
            var database = Substitute.For<DatabaseFacade>(dbContext);
            database.CreateExecutionStrategy().Returns(new DirectExecutionStrategy(dbContext));
            database.BeginTransaction().Returns(Substitute.For<IDbContextTransaction>());
            _context.Database.Returns(database);
            _context.ChangeTracker.Returns(dbContext.ChangeTracker);

            var user = new ClaimsPrincipal(new ClaimsIdentity("TestAuth"));
            _rolePrivilegesChecker.IsAdmin(user).Returns(false);
            _objectFilter.HasPrivilege(prodEnv, user, AccessLevel.Owner).Returns(false);

            // 1. DeleteEnvironment when caller only has Write (not Owner or Admin) throws UnauthorizedAccessException
            Assert.Throws<UnauthorizedAccessException>(() =>
                _environmentsPersistentSource.DeleteEnvironment(new EnvironmentApiModel { EnvironmentName = "PROD" }, user));

            var nonClaimsPrincipal = Substitute.For<System.Security.Principal.IPrincipal>();
            Assert.Throws<UnauthorizedAccessException>(() =>
                _environmentsPersistentSource.DeleteEnvironment(new EnvironmentApiModel { EnvironmentName = "PROD" }, nonClaimsPrincipal));

            // DeleteEnvironment passes the authorization guard when caller is Owner (via IsEnvironmentOwner, HasPrivilege, or IsAdmin)
            var delEnv1 = new Environment { Id = 101, Name = "DEL1", ObjectId = Guid.NewGuid() };
            var delEnv2 = new Environment { Id = 102, Name = "DEL2", ObjectId = Guid.NewGuid() };
            var delEnv3 = new Environment { Id = 103, Name = "DEL3", ObjectId = Guid.NewGuid() };
            environments.AddRange(new[] { delEnv1, delEnv2, delEnv3 });
            _claimsPrincipalReader.GetUserId(user).Returns("owner-id");
            _accessControlPersistentSource.GetAccessControls(delEnv1.ObjectId).Returns(new List<AccessControlApiModel>
            {
                new AccessControlApiModel { Pid = "owner-id", Allow = (int)AccessLevel.Owner, Deny = 0 }
            });
            try { _environmentsPersistentSource.DeleteEnvironment(new EnvironmentApiModel { EnvironmentName = "DEL1" }, user); } catch (Microsoft.Data.SqlClient.SqlException) { }

            _objectFilter.HasPrivilege(delEnv2, user, AccessLevel.Owner).Returns(true);
            try { _environmentsPersistentSource.DeleteEnvironment(new EnvironmentApiModel { EnvironmentName = "DEL2" }, user); } catch (Microsoft.Data.SqlClient.SqlException) { }

            _rolePrivilegesChecker.IsAdmin(user).Returns(true);
            try { _environmentsPersistentSource.DeleteEnvironment(new EnvironmentApiModel { EnvironmentName = "DEL3" }, user); } catch (Microsoft.Data.SqlClient.SqlException) { }
            _rolePrivilegesChecker.IsAdmin(user).Returns(false);

            // 2. CloneEnvironment when non-Admin copies infrastructure mappings from Prod throws UnauthorizedAccessException
            Assert.Throws<UnauthorizedAccessException>(() =>
                _environmentsPersistentSource.CloneEnvironment(new CloneEnvironmentRequest
                {
                    SourceEnvironmentId = 10,
                    NewEnvironmentName = "CLONE_PROD",
                    CopyServerMappings = true
                }, user));

            // 3. CloneEnvironment with CopyPropertyValues skips Secure properties unless caller is a machine principal with explicit ReadSecrets
            _claimsPrincipalReader.IsMachinePrincipal(user).Returns(false);
            _objectFilter.HasPrivilege(devEnv, user, AccessLevel.ReadSecrets).Returns(false);
            _environmentsPersistentSource.CloneEnvironment(new CloneEnvironmentRequest
            {
                SourceEnvironmentId = 20,
                NewEnvironmentName = "CLONE_NO_SECRETS",
                CopyPropertyValues = true,
                CopyServerMappings = false,
                CopyDatabaseMappings = false,
                CopyProjectMappings = false,
                CopyContainerMappings = false,
                CopyCloudResourceMappings = false,
                CopyApiRegistrationMappings = false
            }, user);

            // Only the non-secure property value was copied
            Assert.AreEqual(1, pvList.Count);
            Assert.AreEqual("plain-val", pvList[0].Value);

            // Human caller with a legacy ReadSecrets ACL bit still cannot copy secure property values (S-014a)
            pvList.Clear();
            _objectFilter.HasPrivilege(devEnv, user, AccessLevel.ReadSecrets).Returns(true);
            _environmentsPersistentSource.CloneEnvironment(new CloneEnvironmentRequest
            {
                SourceEnvironmentId = 20,
                NewEnvironmentName = "CLONE_HUMAN_LEGACY_GRANT",
                CopyPropertyValues = true,
                CopyServerMappings = false,
                CopyDatabaseMappings = false,
                CopyProjectMappings = false,
                CopyContainerMappings = false,
                CopyCloudResourceMappings = false,
                CopyApiRegistrationMappings = false
            }, user);
            Assert.AreEqual(1, pvList.Count);
            Assert.AreEqual("plain-val", pvList[0].Value);

            // Machine principal with explicit ReadSecrets on devEnv copies both plain and secure property values
            pvList.Clear();
            _claimsPrincipalReader.IsMachinePrincipal(user).Returns(true);
            _environmentsPersistentSource.CloneEnvironment(new CloneEnvironmentRequest
            {
                SourceEnvironmentId = 20,
                NewEnvironmentName = "CLONE_WITH_SECRETS",
                CopyPropertyValues = true,
                CopyServerMappings = false,
                CopyDatabaseMappings = false,
                CopyProjectMappings = false,
                CopyContainerMappings = false,
                CopyCloudResourceMappings = false,
                CopyApiRegistrationMappings = false
            }, user);
            Assert.AreEqual(2, pvList.Count);
        }

        [TestMethod]
        public void AccessControlAndPagingAndStatusSources_EnforceObjectIdAndDenyGuards()
        {
            var objA = Guid.NewGuid();
            var objB = Guid.NewGuid();
            var acRow = new AccessControl { Id = 55, ObjectId = objA, Name = "UserA", Sid = "sid-1", Allow = 7, Deny = 1 };
            var acList = new List<AccessControl> { acRow };
            var envList = new List<Environment>();
            var envSet = DbContextMock.GetQueryableMockDbSet(envList);
            var acSet = DbContextMock.GetQueryableMockDbSet(acList);
            acSet.Find(55).Returns(acRow);
            acSet.Find(999).Returns((AccessControl)null!);
            var reqList = new List<DeploymentRequest>();
            var reqSet = DbContextMock.GetQueryableMockDbSet(reqList);
            var histSet = DbContextMock.GetQueryableMockDbSet(new List<EnvironmentHistory>());
            var srvList = new List<Server>();
            var srvSet = DbContextMock.GetQueryableMockDbSet(srvList);
            var propList = new List<Property>();
            var propSet = DbContextMock.GetQueryableMockDbSet(propList);
            var propFilterList = new List<PropertyFilter>();
            var propFilterSet = DbContextMock.GetQueryableMockDbSet(propFilterList);
            var pvList = new List<PropertyValue>();
            var pvSet = DbContextMock.GetQueryableMockDbSet(pvList);
            var pvfList = new List<PropertyValueFilter>();
            var pvfSet = DbContextMock.GetQueryableMockDbSet(pvfList);

            _context.AccessControls.Returns(acSet);
            _context.Environments.Returns(envSet);
            _context.DeploymentRequests.Returns(reqSet);
            _context.EnvironmentHistories.Returns(histSet);
            _context.Servers.Returns(srvSet);
            _context.Properties.Returns(propSet);
            _context.PropertyFilters.Returns(propFilterSet);
            _context.PropertyValues.Returns(pvSet);
            _context.PropertyValueFilters.Returns(pvfSet);

            var acSource = new AccessControlPersistentSource(_contextFactory);
            var user = new ClaimsPrincipal(new ClaimsIdentity("TestAuth"));

            // Foreign ObjectId or missing Id on UpdateAccessControl and DeleteAccessControl is rejected
            Assert.IsNull(acSource.UpdateAccessControl(new AccessControlApiModel { Id = 999, Name = "Missing", Allow = 4 }, objA, user));
            Assert.IsNull(acSource.UpdateAccessControl(new AccessControlApiModel { Id = 55, Name = "Tamper", Allow = 4 }, objB, user));
            Assert.IsNotNull(acSource.UpdateAccessControl(new AccessControlApiModel { Id = 55, Name = "ValidUpdate", Allow = 7, Deny = 1 }, objA, user));

            Assert.AreEqual(Guid.Empty, acSource.DeleteAccessControl(999, objA, user));
            Assert.AreEqual(Guid.Empty, acSource.DeleteAccessControl(55, objB, user));
            Assert.AreEqual(objA, acSource.DeleteAccessControl(55, objA, user));
            acList.Add(acRow);

            // RequestsStatusPersistentSource.GetDeploymentRequestApiModels enforces Deny != 0
            var env = new Environment { Id = 1, Name = "DeniedEnv", ObjectId = objA };
            var req = new DeploymentRequest { Id = 100, Environment = "DeniedEnv", Project = "Proj", Status = "Pending" };
            envList.Add(env);
            reqList.Add(req);

            var reqModels = RequestsStatusPersistentSource.GetDeploymentRequestApiModels(_context, "user1", new List<string> { "sid-1" }).ToList();
            Assert.AreEqual(1, reqModels.Count);
            Assert.IsFalse(reqModels[0].UserEditable);

            // IsEnvironmentOwner all principal match branches (Pid == userId, Pid == userOid, Sid == userId, Sid == userlogin, Sid in userSids, null userSids)
            var claimUser = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("oid", "oid-claim") }));
            _claimsPrincipalReader.GetSidsForUser(claimUser).Returns((List<string>)null!);
            _claimsPrincipalReader.GetUserId(claimUser).Returns("uid-1");
            _claimsPrincipalReader.GetUserLogin(claimUser).Returns("login-1");

            foreach (var acl in new[]
            {
                new AccessControlApiModel { Pid = "uid-1", Allow = (int)AccessLevel.Owner },
                new AccessControlApiModel { Pid = "oid-claim", Allow = (int)AccessLevel.Owner },
                new AccessControlApiModel { Sid = "uid-1", Allow = (int)AccessLevel.Owner },
                new AccessControlApiModel { Sid = "login-1", Allow = (int)AccessLevel.Owner }
            })
            {
                _accessControlPersistentSource.GetAccessControls(objA).Returns(new List<AccessControlApiModel> { acl });
                Assert.IsTrue(_environmentsPersistentSource.IsEnvironmentOwner("DeniedEnv", claimUser));
            }

            _claimsPrincipalReader.GetSidsForUser(claimUser).Returns(new List<string> { "group-sid" });
            _accessControlPersistentSource.GetAccessControls(objA).Returns(new List<AccessControlApiModel>
            {
                new AccessControlApiModel { Sid = "group-sid", Allow = (int)AccessLevel.Owner },
                new AccessControlApiModel { Pid = "other-pid", Sid = "other-sid", Allow = (int)AccessLevel.Owner },
                new AccessControlApiModel { Pid = null, Sid = null, Allow = (int)AccessLevel.Owner }
            });
            Assert.IsTrue(_environmentsPersistentSource.IsEnvironmentOwner("DeniedEnv", claimUser));

            // CreateEnvironment & UpdateEnvironment cover MapToEnvironment (isNewEnvironment true/false, SID/PID, null owner)
            envList.Clear();
            var created = _environmentsPersistentSource.CreateEnvironment(new EnvironmentApiModel
            {
                EnvironmentName = "BrandNewEnv",
                Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "owner", EnvironmentOwnerId = "S-1-5-21-1" }
            }, user);
            Assert.IsNotNull(created);
            Assert.AreEqual(1, envList[0].AccessControls.Count);
            Assert.AreEqual((int)AccessLevel.Owner, envList[0].AccessControls.First().Allow);

            var createdPid = _environmentsPersistentSource.CreateEnvironment(new EnvironmentApiModel
            {
                EnvironmentName = "BrandNewPidEnv",
                Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "owner", EnvironmentOwnerId = "pid-123" }
            }, user);
            Assert.IsNotNull(createdPid);

            var createdNoOwner = _environmentsPersistentSource.CreateEnvironment(new EnvironmentApiModel
            {
                EnvironmentName = "BrandNewNoOwnerEnv",
                Details = new EnvironmentDetailsApiModel { EnvironmentOwner = "" }
            }, user);
            Assert.IsNotNull(createdNoOwner);

            envList[0].Id = 500;
            envList[1].Id = 501;
            envList[2].Id = 502;
            _environmentsPersistentSource.UpdateEnvironment(new EnvironmentApiModel
            {
                EnvironmentId = 500,
                EnvironmentName = "BrandNewEnv",
                Details = new EnvironmentDetailsApiModel { Description = "Updated" }
            }, user);

            // PagingPersistentSourceBase (via ServersPersistentSource) and PropertyValuesPersistentSource enforce Deny != 0
            var envAllowObj = Guid.NewGuid();
            var envDenyObj = Guid.NewGuid();
            var envAllow = new Environment { Id = 1, Name = "EnvAllow", ObjectId = envAllowObj };
            var envDeny = new Environment { Id = 2, Name = "EnvDeny", ObjectId = envDenyObj };
            var srvAllow = new Server { Id = 1, Name = "SrvAllow", Environments = new List<Environment> { envAllow }, TagLinks = new List<ServerTag>() };
            var srvDeny = new Server { Id = 2, Name = "SrvDeny", Environments = new List<Environment> { envDeny }, TagLinks = new List<ServerTag>() };

            _claimsPrincipalReader.GetSidsForUser(user).Returns(new List<string> { "sid-1" });
            _claimsPrincipalReader.GetUserLogin(user).Returns("user1");
            _rolePrivilegesChecker.IsAdmin(user).Returns(false);

            envList.Clear();
            envList.AddRange(new[] { envAllow, envDeny });
            acList.Clear();
            acList.AddRange(new[]
            {
                new AccessControl { Id = 1, ObjectId = envAllowObj, Sid = "sid-1", Allow = (int)(AccessLevel.Owner | AccessLevel.Write), Deny = 0 },
                new AccessControl { Id = 2, ObjectId = envDenyObj, Sid = "sid-1", Allow = (int)(AccessLevel.Owner | AccessLevel.Write), Deny = (int)AccessLevel.Write }
            });
            srvList.AddRange(new[] { srvAllow, srvDeny });

            var serversSource = new ServersPersistentSource(_contextFactory, _rolePrivilegesChecker, _claimsPrincipalReader);
            var pagedServers = serversSource.GetServerApiModelByPage(10, 1, new PagedDataOperators(), user);
            Assert.IsTrue(pagedServers.Items.Single(s => s.Name == "SrvAllow").UserEditable);
            Assert.IsFalse(pagedServers.Items.Single(s => s.Name == "SrvDeny").UserEditable);

            var singleSrvAllow = serversSource.GetServer(1, user);
            var singleSrvDeny = serversSource.GetServer(2, user);
            Assert.IsTrue(singleSrvAllow.UserEditable);
            Assert.IsFalse(singleSrvDeny.UserEditable);

            var prop = new Property { Id = 1, Name = "Prop1", Secure = false };
            var secProp = new Property { Id = 2, Name = "SecProp1", Secure = true };
            var parentPlainProp = new Property { Id = 3, Name = "ParentPlainProp", Secure = false };
            var parentSecProp = new Property { Id = 4, Name = "ParentSecProp", Secure = true };
            var globalPlainProp = new Property { Id = 5, Name = "GlobalPlainProp", Secure = false };
            var globalSecProp = new Property { Id = 6, Name = "GlobalSecProp", Secure = true };
            var propFilter = new PropertyFilter { Id = 1, Name = "environment" };
            var pvAllow = new PropertyValue { Id = 1, Value = "v1", Property = prop };
            var pvDeny = new PropertyValue { Id = 2, Value = "v2", Property = parentPlainProp };
            var pvSecAllow = new PropertyValue { Id = 3, Value = "cipher-env", Property = secProp };
            var pvGlobalPlain = new PropertyValue { Id = 4, Value = "v-global", Property = globalPlainProp, Filters = new List<PropertyValueFilter>() };
            var pvGlobalSec = new PropertyValue { Id = 5, Value = "cipher-global", Property = globalSecProp, Filters = new List<PropertyValueFilter>() };
            var pvParentSec = new PropertyValue { Id = 6, Value = "cipher-parent", Property = parentSecProp };
            var pvfAllow = new PropertyValueFilter { Id = 1, Value = "EnvAllow", PropertyFilter = propFilter, PropertyValue = pvAllow };
            var pvfDeny = new PropertyValueFilter { Id = 2, Value = "EnvDeny", PropertyFilter = propFilter, PropertyValue = pvDeny };
            var pvfSecAllow = new PropertyValueFilter { Id = 3, Value = "EnvAllow", PropertyFilter = propFilter, PropertyValue = pvSecAllow };
            var pvfParentSec = new PropertyValueFilter { Id = 4, Value = "EnvDeny", PropertyFilter = propFilter, PropertyValue = pvParentSec };
            pvAllow.Filters = new List<PropertyValueFilter> { pvfAllow };
            pvDeny.Filters = new List<PropertyValueFilter> { pvfDeny };
            pvSecAllow.Filters = new List<PropertyValueFilter> { pvfSecAllow };
            pvParentSec.Filters = new List<PropertyValueFilter> { pvfParentSec };

            propList.AddRange(new[] { prop, secProp, parentPlainProp, parentSecProp, globalPlainProp, globalSecProp });
            propFilterList.Add(propFilter);
            pvList.AddRange(new[] { pvAllow, pvDeny, pvSecAllow, pvGlobalPlain, pvGlobalSec, pvParentSec });
            pvfList.AddRange(new[] { pvfAllow, pvfDeny, pvfSecAllow, pvfParentSec });

            var propSource = new PropertyValuesPersistentSource(_contextFactory, Substitute.For<IPropertyEncryptor>(), _claimsPrincipalReader);
            var scopedAllow = propSource.GetPropertyValuesForScopeByPage(10, 1, new PagedDataOperators(), new EnvironmentApiModel
            {
                EnvironmentName = "EnvAllow",
                EnvironmentSecure = false,
                ParentId = 2
            }, user);
            var scopedDeny = propSource.GetPropertyValuesForScopeByPage(10, 1, new PagedDataOperators(), new EnvironmentApiModel { EnvironmentName = "EnvDeny", EnvironmentSecure = true }, user);
            Assert.IsTrue(scopedAllow.Items.Where(i => i.PropertyValueScope == "EnvAllow").All(i => i.UserEditable));
            Assert.AreEqual("v1", scopedAllow.Items.Single(i => i.Property == "Prop1" && i.PropertyValueScope == "EnvAllow").PropertyValue);
            Assert.AreEqual(string.Empty, scopedAllow.Items.Single(i => i.Property == "SecProp1" && i.PropertyValueScope == "EnvAllow").PropertyValue);
            Assert.AreEqual("v2", scopedAllow.Items.Single(i => i.Property == "ParentPlainProp" && i.PropertyValueScope == "EnvDeny").PropertyValue);
            Assert.AreEqual(string.Empty, scopedAllow.Items.Single(i => i.Property == "ParentSecProp" && i.PropertyValueScope == "EnvDeny").PropertyValue);
            Assert.AreEqual("v-global", scopedAllow.Items.Single(i => i.Property == "GlobalPlainProp" && i.PropertyValueScope == null).PropertyValue);
            Assert.AreEqual(string.Empty, scopedAllow.Items.Single(i => i.Property == "GlobalSecProp" && i.PropertyValueScope == null).PropertyValue);
            Assert.IsTrue(scopedDeny.Items.Where(i => i.PropertyValueScope == "EnvDeny").All(i => !i.UserEditable));

            var searched = propSource.GetPropertyValuesForSearchValueByPage(10, 1, new PagedDataOperators(), user);
            Assert.IsTrue(searched.Items.Where(i => i.PropertyValueScope == "EnvAllow").All(i => i.UserEditable));
            Assert.IsTrue(searched.Items.Where(i => i.PropertyValueScope == "EnvDeny").All(i => !i.UserEditable));
            Assert.IsTrue(searched.Items.Where(i => i.Secure).All(i => i.PropertyValue == string.Empty));
        }

        [TestMethod]
        public void SetExecutionIdentityReference_UpdatesEnvironmentAndRecordsHistory()
        {
            var environment = new Environment
            {
                Id = 7,
                Name = "Payments",
                ExecutionIdentityReference = "shared-prod"
            };
            var environments = new List<Environment> { environment };
            var histories = new List<EnvironmentHistory>();
            var environmentsDbSet = DbContextMock.GetQueryableMockDbSet(environments);
            var historiesDbSet = DbContextMock.GetQueryableMockDbSet(histories);
            _context.Environments.Returns(environmentsDbSet);
            _context.EnvironmentHistories.Returns(historiesDbSet);

            var result = _environmentsPersistentSource.SetExecutionIdentityReference(
                7,
                "payments-prod",
                _user);

            Assert.AreEqual("payments-prod", environment.ExecutionIdentityReference);
            Assert.AreEqual("payments-prod", result.ExecutionIdentityReference);
            Assert.AreEqual(1, histories.Count);
            Assert.AreEqual("shared-prod", histories[0].FromValue);
            Assert.AreEqual("payments-prod", histories[0].ToValue);
            Assert.AreEqual("Execution Identity Update", histories[0].UpdateType);
            Assert.AreEqual("testuser@domain.com", histories[0].UpdatedBy);
            _context.Received(1).SaveChanges();

            var allLoggedMessages = _logger.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
                .Select(call => (Level: (LogLevel)call.GetArguments()[0]!, Message: call.GetArguments()[2]?.ToString() ?? string.Empty))
                .ToList();
            var infoMessages = allLoggedMessages
                .Where(entry => entry.Level == LogLevel.Information)
                .Select(entry => entry.Message)
                .ToList();
            Assert.AreEqual(1, infoMessages.Count);
            StringAssert.Contains(infoMessages[0], "Payments");
            StringAssert.Contains(infoMessages[0], "payments-prod");
            Assert.IsFalse(
                allLoggedMessages.Any(entry => entry.Message.Contains("testuser@domain.com", StringComparison.OrdinalIgnoreCase)),
                "Application logs must not expose user email/PII; durable audit attribution belongs in EnvironmentHistory.");
        }

        [TestMethod]
        public void SetExecutionIdentityReference_EmptyReferenceClearsAndAudits()
        {
            var environment = new Environment
            {
                Id = 7,
                Name = "Payments",
                ExecutionIdentityReference = "payments-prod"
            };
            var histories = new List<EnvironmentHistory>();
            var environmentsDbSet =
                DbContextMock.GetQueryableMockDbSet(new List<Environment> { environment });
            var historiesDbSet = DbContextMock.GetQueryableMockDbSet(histories);
            _context.Environments.Returns(environmentsDbSet);
            _context.EnvironmentHistories.Returns(historiesDbSet);

            _environmentsPersistentSource.SetExecutionIdentityReference(7, string.Empty, _user);

            Assert.IsNull(environment.ExecutionIdentityReference);
            Assert.AreEqual("payments-prod", histories.Single().FromValue);
            Assert.AreEqual(string.Empty, histories.Single().ToValue);
            StringAssert.Contains(histories.Single().Details, "cleared");
        }

        [TestMethod]
        [DataRow(" ")]
        [DataRow("../secret")]
        [DataRow("contains/slash")]
        [DataRow("contains space")]
        [DataRow("payments\n")]
        [DataRow("payments\r\n")]
        [DataRow("pay\nments")]
        [DataRow("\npayments")]
        public void SetExecutionIdentityReference_RejectsInvalidReference(string reference)
        {
            Assert.ThrowsExactly<ArgumentException>(() =>
                _environmentsPersistentSource.SetExecutionIdentityReference(7, reference, _user));

            _context.DidNotReceive().SaveChanges();
        }

        [TestMethod]
        public void SetExecutionIdentityReference_RejectsReferenceLongerThanDatabaseColumn()
        {
            Assert.ThrowsExactly<ArgumentException>(() =>
                _environmentsPersistentSource.SetExecutionIdentityReference(
                    7,
                    new string('a', 129),
                    _user));
        }

        [TestMethod]
        public void SetExecutionIdentityReference_RejectsMissingEnvironment()
        {
            var environmentsDbSet =
                DbContextMock.GetQueryableMockDbSet(new List<Environment>());
            _context.Environments.Returns(environmentsDbSet);

            Assert.ThrowsExactly<KeyNotFoundException>(() =>
                _environmentsPersistentSource.SetExecutionIdentityReference(404, "payments-prod", _user));
        }

        [TestMethod]
        public void SetExecutionIdentityReference_RejectsNonAdministrator()
        {
            _rolePrivilegesChecker.IsAdmin(_user).Returns(false);
            _contextFactory.ClearReceivedCalls();

            Assert.ThrowsExactly<UnauthorizedAccessException>(() =>
                _environmentsPersistentSource.SetExecutionIdentityReference(7, "payments-prod", _user));

            _contextFactory.DidNotReceive().GetContext();
        }

        [TestMethod]
        public void SetExecutionIdentityReference_FollowedByAddHistoryWithEmptyBackup_PreservesRestoredFromBackup()
        {
            var environment = new Environment
            {
                Id = 7,
                Name = "Payments",
                RestoredFromBackup = "PROD_20261101.bak"
            };
            var unbackedEnvironment = new Environment
            {
                Id = 8,
                Name = "Greenfield",
                RestoredFromBackup = null
            };
            var environments = new List<Environment> { environment, unbackedEnvironment };
            var histories = new List<EnvironmentHistory>();
            _context.Environments.Returns(_ => DbContextMock.GetQueryableMockDbSet(environments));
            _context.EnvironmentHistories.Returns(_ => DbContextMock.GetQueryableMockDbSet(histories));

            _environmentsPersistentSource.SetExecutionIdentityReference(7, "payments-prod", _user);
            _environmentsPersistentSource.SetExecutionIdentityReference(8, "greenfield-dev", _user);

            EnvironmentHistoryPersistentSource.AddHistory(
                environment,
                string.Empty,
                "Environment added to project PaymentsApp",
                "testuser@domain.com",
                "Add Environment Mapping To Project",
                _context);

            var historySource = new EnvironmentHistoryPersistentSource(
                Substitute.For<ILogger<EnvironmentHistoryPersistentSource>>(),
                _contextFactory);
            Assert.IsTrue(historySource.UpdateHistory(
                "Payments",
                string.Empty,
                "Manual comment",
                "testuser@domain.com",
                "Comment"));
            Assert.IsTrue(historySource.UpdateHistory(
                "Greenfield",
                string.Empty,
                "Manual comment",
                "testuser@domain.com",
                "Comment"));

            Assert.AreEqual("PROD_20261101.bak", environment.RestoredFromBackup);
            Assert.AreEqual("PROD_20261101.bak", histories[2].FromValue);
            Assert.AreEqual("PROD_20261101.bak", histories[2].ToValue);
            Assert.AreEqual("PROD_20261101.bak", histories[3].FromValue);
            Assert.AreEqual("PROD_20261101.bak", histories[3].ToValue);

            Assert.AreEqual(string.Empty, unbackedEnvironment.RestoredFromBackup);
            Assert.AreEqual(string.Empty, histories[4].FromValue);
            Assert.AreEqual(string.Empty, histories[4].ToValue);
        }
    }
}