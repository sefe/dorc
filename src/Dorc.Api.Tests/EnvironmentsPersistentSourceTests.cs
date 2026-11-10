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
    }
} 