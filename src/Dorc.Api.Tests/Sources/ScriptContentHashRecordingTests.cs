using Dorc.Api.Controllers;
using Dorc.Api.Tests.Mocks;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Security;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Security.Claims;
using System.Security.Principal;
using System.Text.Json;

namespace Dorc.Api.Tests.Sources
{
    /// <summary>
    /// The recorded content hash is what a script is permitted to be. Three properties follow
    /// from that and are the reason this is not folded into the ordinary script edit.
    ///
    /// It must be settable on its own, by a promotion pipeline that has just published a new
    /// version — otherwise every legitimate promotion looks like tampering at the next
    /// deployment. It must be clearable, so a script whose new hash nobody knows can be
    /// re-baselined by the next execution. And it must be audited, because a baseline that
    /// changes without a promotion behind it is exactly what an investigation would look for.
    /// </summary>
    [TestClass]
    public class ScriptContentHashRecordingTests
    {
        private const string ValidHash =
            "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

        private List<Script> _stored = null!;
        private List<AuditScript> _storedAudits = null!;
        private DbContext _efContext = null!;
        private DatabaseFacade _database = null!;
        private IDbContextTransaction _transaction = null!;
        private IDeploymentContext _context = null!;
        private IScriptsAuditPersistentSource _audit = null!;
        private ScriptsPersistentSource _source = null!;
        private IPrincipal _user = null!;

        [TestInitialize]
        public void Setup()
        {
            _stored = new List<Script>
            {
                new()
                {
                    Id = 1,
                    Name = "Deploy.ps1",
                    Path = "Deploy.ps1",
                    Components = new List<Component>()
                }
            };
            _storedAudits = new List<AuditScript>();

            // Built into locals first: GetQueryableMockDbSet configures another substitute
            // internally, and NSubstitute cannot have that happen inside a Returns() argument.
            var scripts = DbContextMock.GetQueryableMockDbSet(_stored);
            var auditScripts = DbContextMock.GetQueryableMockDbSet(_storedAudits);

            _efContext = new DbContext(
                new DbContextOptionsBuilder<DbContext>().UseSqlServer("Server=.;Database=Dummy;").Options);
            _database = Substitute.For<DatabaseFacade>(_efContext);
            _transaction = Substitute.For<IDbContextTransaction>();
            _database.CreateExecutionStrategy().Returns(new PassthroughExecutionStrategy(_efContext));
            _database.BeginTransaction().Returns(_transaction);

            _context = Substitute.For<IDeploymentContext>();
            _context.Scripts.Returns(scripts);
            _context.AuditScripts.Returns(auditScripts);
            _context.Database.Returns(_database);
            _context.ChangeTracker.Returns(_efContext.ChangeTracker);
            _context.RecordScriptContentHashIfUnrecorded(
                    Arg.Any<int>(), Arg.Any<string>())
                .Returns(call =>
                {
                    var script = _stored.FirstOrDefault(candidate =>
                        candidate.Id == call.ArgAt<int>(0));

                    if (script == null || script.ContentHash != null)
                    {
                        return 0;
                    }

                    script.ContentHash = call.ArgAt<string>(1);
                    return 1;
                });
            _context.RecordScriptContentHashIfUnrecorded(
                    Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(call =>
                {
                    var script = _stored.FirstOrDefault(candidate =>
                        candidate.Id == call.ArgAt<int>(0));

                    if (script == null
                        || script.ContentHash != null
                        || !string.Equals(script.Path, call.ArgAt<string>(1), StringComparison.Ordinal))
                    {
                        return 0;
                    }

                    script.ContentHash = call.ArgAt<string>(2);
                    return 1;
                });

            var contextFactory = Substitute.For<IDeploymentContextFactory>();
            contextFactory.GetContext().Returns(_context);

            var claimsReader = Substitute.For<IClaimsPrincipalReader>();
            claimsReader.GetUserFullDomainName(Arg.Any<IPrincipal>()).Returns(@"CORP\promoter");

            _audit = Substitute.For<IScriptsAuditPersistentSource>();
            _user = Substitute.For<IPrincipal>();

            _source = new ScriptsPersistentSource(contextFactory, claimsReader, _audit);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _efContext?.Dispose();
        }

        [TestMethod]
        public void FirstUseRecordsOnlyWhileTheBaselineIsUnrecorded()
        {
            var authoritative =
                _source.RecordContentHashIfUnrecorded(1, ValidHash, _user);

            Assert.AreEqual(ValidHash, authoritative);
            Assert.AreEqual(ValidHash, _stored.Single().ContentHash);
            Assert.AreEqual(1, _storedAudits.Count);
            var auditEntry = _storedAudits.Single();
            Assert.AreEqual(1L, auditEntry.ScriptId);
            Assert.AreEqual("Deploy.ps1", auditEntry.ScriptName);
            Assert.AreEqual("ContentHash=(unrecorded)", auditEntry.FromValue);
            Assert.AreEqual($"ContentHash={ValidHash}", auditEntry.ToValue);
            Assert.AreEqual(@"CORP\promoter", auditEntry.UpdatedBy);
            Assert.AreEqual("RecordFirstContentHash", auditEntry.Type);
            _transaction.Received(1).Commit();
            _audit.DidNotReceiveWithAnyArgs().AddRecord(
                default, default!, default!, default!, default!, default!, default!);
        }

        [TestMethod]
        public void FirstUseReturnsAConcurrentWinnersBaselineWithoutOverwritingIt()
        {
            const string losingCandidate =
                "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
            _context.RecordScriptContentHashIfUnrecorded(1, losingCandidate)
                .Returns(_ =>
                {
                    _stored.Single().ContentHash = ValidHash;
                    return 0;
                });

            var authoritative =
                _source.RecordContentHashIfUnrecorded(1, losingCandidate, _user);

            Assert.AreEqual(ValidHash, authoritative);
            Assert.AreEqual(ValidHash, _stored.Single().ContentHash);
            Assert.AreEqual(0, _storedAudits.Count);
            _audit.DidNotReceiveWithAnyArgs().AddRecord(
                default, default!, default!, default!, default!, default!, default!);
        }

        [TestMethod]
        public void RecordsAHashAgainstTheScript()
        {
            Assert.IsTrue(_source.RecordContentHash(1, ValidHash, _user));

            Assert.AreEqual(ValidHash, _stored.Single().ContentHash);
        }

        [TestMethod]
        public void AdministrativeRecordingCanDeliberatelyReplaceAnExistingBaseline()
        {
            const string replacement =
                "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
            _source.RecordContentHash(1, ValidHash, _user);

            Assert.IsTrue(_source.RecordContentHash(1, replacement, _user));

            Assert.AreEqual(replacement, _stored.Single().ContentHash);
        }

        /// <summary>
        /// A pipeline handing over an upper-case hash is not making a mistake, and a stored
        /// baseline that differed only in case from the one the runner computes would refuse a
        /// script that had never changed.
        /// </summary>
        [TestMethod]
        public void StoresAHashInOneCaseWhateverCaseItArrivesIn()
        {
            _source.RecordContentHash(1, ValidHash.ToUpperInvariant(), _user);

            Assert.AreEqual(ValidHash, _stored.Single().ContentHash);
        }

        /// <summary>
        /// Refused where it is recorded rather than discovered as a mismatch on the next
        /// deployment, when the report would say the script had changed and it had not.
        /// </summary>
        [TestMethod]
        [DataRow("not-a-hash")]
        [DataRow("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a0")]
        [DataRow("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08a")]
        [DataRow("zf86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")]
        public void RefusesAValueThatIsNotShapedLikeASha256Hash(string candidate)
        {
            Assert.IsFalse(_source.RecordContentHash(1, candidate, _user));

            Assert.IsNull(_stored.Single().ContentHash);
        }

        [TestMethod]
        public void RefusesAScriptThatDoesNotExist()
        {
            Assert.IsFalse(_source.RecordContentHash(99, ValidHash, _user));
        }

        /// <summary>
        /// Clearing means "re-record on next execution", not "stop verifying this one". There is
        /// no per-script opt-out, because a per-script opt-out is a per-script hole.
        /// </summary>
        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void ClearsTheRecordedHash(string? cleared)
        {
            _source.RecordContentHash(1, ValidHash, _user);

            Assert.IsTrue(_source.RecordContentHash(1, cleared, _user));
            Assert.IsNull(_stored.Single().ContentHash);
        }

        [TestMethod]
        public void AuditsTheChangeWithBothTheOldAndTheNewBaseline()
        {
            _source.RecordContentHash(1, ValidHash, _user);

            Assert.AreEqual(1, _storedAudits.Count);
            var auditEntry = _storedAudits.Single();
            Assert.AreEqual(1L, auditEntry.ScriptId);
            Assert.AreEqual("Deploy.ps1", auditEntry.ScriptName);
            StringAssert.Contains(auditEntry.FromValue, "(unrecorded)");
            StringAssert.Contains(auditEntry.ToValue, ValidHash);
            Assert.AreEqual(@"CORP\promoter", auditEntry.UpdatedBy);
            Assert.AreEqual("RecordContentHash", auditEntry.Type);
            _context.Received(1).SaveChanges();
            _transaction.Received(1).Commit();
            _audit.DidNotReceiveWithAnyArgs().AddRecord(
                default, default!, default!, default!, default!, default!, default!);
        }

        /// <summary>
        /// A pipeline that re-promotes an unchanged artefact would otherwise fill the audit trail
        /// with entries saying nothing happened, which is how a trail stops being read.
        /// </summary>
        [TestMethod]
        public void RecordingTheSameHashAgainIsNotAudited()
        {
            _source.RecordContentHash(1, ValidHash, _user);
            _storedAudits.Clear();

            Assert.IsTrue(_source.RecordContentHash(1, ValidHash, _user));

            Assert.AreEqual(0, _storedAudits.Count);
            _audit.DidNotReceiveWithAnyArgs().AddRecord(
                default, default!, default!, default!, default!, default!, default!);
        }

        /// <summary>
        /// The general script edit changes non-path metadata such as display name, PowerShell
        /// version, or NonProdOnly while the target path stays the same: the recorded baseline
        /// must remain intact and any caller-supplied ContentHash on the ordinary DTO must be
        /// ignored.
        /// </summary>
        [TestMethod]
        public void AnOrdinaryScriptEditLeavesTheBaselineAlone()
        {
            _source.RecordContentHash(1, ValidHash, _user);

            _source.UpdateScript(new ScriptApiModel
            {
                Id = 1,
                Name = "RenamedDisplayName",
                Path = "Deploy.ps1",
                NonProdOnly = true,
                PowerShellVersionNumber = "7.4",
                ContentHash = null
            }, _user);

            Assert.AreEqual(ValidHash, _stored.Single().ContentHash);
        }

        [TestMethod]
        [DataRow("Other.ps1")]
        [DataRow("deploy.ps1")]
        public void ChangingTheScriptPathInUpdateScriptClearsTheBaselineAndAuditsTheReset(string nextPath)
        {
            _source.RecordContentHash(1, ValidHash, _user);
            _audit.ClearReceivedCalls();

            Assert.IsTrue(_source.UpdateScript(new ScriptApiModel
            {
                Id = 1,
                Name = nextPath,
                Path = nextPath,
                PowerShellVersionNumber = "7.4",
                ContentHash = ValidHash
            }, _user));

            Assert.IsNull(_stored.Single().ContentHash);
            _audit.Received(1).AddRecord(
                1,
                nextPath,
                Arg.Is<string>(from => from.Contains($"ContentHash={ValidHash}")),
                Arg.Is<string>(to => to.Contains("ContentHash=(unrecorded)")),
                @"CORP\promoter",
                "Update",
                Arg.Any<string>());
        }

        [TestMethod]
        [DataRow("NewTarget.ps1")]
        [DataRow("deploy.ps1")]
        [DataRow("Deploy.ps1   ")]
        public void FirstUseWithExpectedPathRefusesToPoisonScriptMovedToAnotherPath(string livePath)
        {
            _stored.Single().Path = livePath;

            var authoritative = _source.RecordContentHashIfUnrecorded(
                1,
                expectedPath: "Deploy.ps1",
                ValidHash,
                _user);

            Assert.IsNull(authoritative);
            Assert.IsNull(_stored.Single().ContentHash);
            Assert.AreEqual(0, _storedAudits.Count);
            _transaction.Received(1).Rollback();
            _audit.DidNotReceiveWithAnyArgs().AddRecord(
                default, default!, default!, default!, default!, default!, default!);
        }

        [TestMethod]
        public void RecordContentHash_RollsBackTransactionWhenSaveFails_AndRetriesCleanlyViaExecutionStrategy()
        {
            var attempts = 0;
            var txAttempt1 = Substitute.For<IDbContextTransaction>();
            var txAttempt2 = Substitute.For<IDbContextTransaction>();
            _database.BeginTransaction().Returns(txAttempt1, txAttempt2);
            _database.CreateExecutionStrategy().Returns(new ReplayOnceExecutionStrategy(_efContext));

            _context.SaveChanges().Returns(_ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    // Simulate transient commit failure after in-memory mutations happened on attempt 1:
                    // rollback resets DB state before strategy replays attempt 2.
                    _stored.Single().ContentHash = null;
                    _storedAudits.Clear();
                    throw new InvalidOperationException("Simulated transient commit failure");
                }

                return 2;
            });

            Assert.IsTrue(_source.RecordContentHash(1, ValidHash, _user));

            Assert.AreEqual(2, attempts);
            Assert.AreEqual(ValidHash, _stored.Single().ContentHash);
            Assert.AreEqual(1, _storedAudits.Count);
            txAttempt1.Received(1).Rollback();
            txAttempt1.DidNotReceive().Commit();
            txAttempt2.Received(1).Commit();
        }

        [TestMethod]
        public void RecordContentHash_ReverifiesProjectMembershipInsideTransaction_BlockingConcurrentProjectLink()
        {
            var projectA = new Project { Id = 10, Name = "TeamA" };
            var projectB = new Project { Id = 20, Name = "TeamB" };
            var component = new Component
            {
                Id = 100,
                Name = "SharedDeploy",
                Projects = new List<Project> { projectA }
            };
            _stored.Single().Components = new List<Component> { component };

            // Concurrent link attaches projectB right as the transaction begins.
            _database.BeginTransaction().Returns(_ =>
            {
                component.Projects.Add(projectB);
                return _transaction;
            });

            Assert.Throws<UnauthorizedAccessException>(() =>
                _source.RecordContentHash(
                    1,
                    ValidHash,
                    expectedPath: "Deploy.ps1",
                    _user,
                    projectAuthorize: projectId => projectId == 10));

            Assert.IsNull(_stored.Single().ContentHash);
            Assert.AreEqual(0, _storedAudits.Count);
            _transaction.Received(1).Rollback();
            _transaction.DidNotReceive().Commit();
        }

        [TestMethod]
        [DataRow("StalePath.ps1")]
        [DataRow("deploy.ps1")]
        [DataRow(" Deploy.ps1")]
        [DataRow("Deploy.ps1 ")]
        public void RecordContentHash_ExpectedPathMismatch_ThrowsInvalidOperationAndRollsBack(string staleExpectedPath)
        {
            Assert.Throws<InvalidOperationException>(() =>
                _source.RecordContentHash(
                    1,
                    ValidHash,
                    expectedPath: staleExpectedPath,
                    _user,
                    projectAuthorize: null));

            Assert.IsNull(_stored.Single().ContentHash);
            Assert.AreEqual(0, _storedAudits.Count);
            _transaction.Received(1).Rollback();
            _transaction.DidNotReceive().Commit();
        }

        [TestMethod]
        public void RecordContentHash_ExpectedPathMatchingExactOrdinal_RecordsHashAndAudit()
        {
            Assert.IsTrue(_source.RecordContentHash(
                1,
                ValidHash,
                expectedPath: "Deploy.ps1",
                _user,
                projectAuthorize: null));

            Assert.AreEqual(ValidHash, _stored.Single().ContentHash);
            Assert.AreEqual(1, _storedAudits.Count);
            _transaction.Received(1).Commit();
        }

        private sealed class PassthroughExecutionStrategy(DbContext context) : IExecutionStrategy
        {
            public bool RetriesOnFailure => false;

            public TResult Execute<TState, TResult>(
                TState state,
                Func<DbContext, TState, TResult> operation,
                Func<DbContext, TState, ExecutionResult<TResult>>? verifySucceeded) =>
                operation(context, state);

            public Task<TResult> ExecuteAsync<TState, TResult>(
                TState state,
                Func<DbContext, TState, CancellationToken, Task<TResult>> operation,
                Func<DbContext, TState, CancellationToken, Task<ExecutionResult<TResult>>>? verifySucceeded,
                CancellationToken cancellationToken = default) =>
                operation(context, state, cancellationToken);
        }

        private sealed class ReplayOnceExecutionStrategy(DbContext context) : IExecutionStrategy
        {
            private int _failures;

            public bool RetriesOnFailure => true;

            public TResult Execute<TState, TResult>(
                TState state,
                Func<DbContext, TState, TResult> operation,
                Func<DbContext, TState, ExecutionResult<TResult>>? verifySucceeded)
            {
                try
                {
                    return operation(context, state);
                }
                catch when (_failures++ == 0)
                {
                    return operation(context, state);
                }
            }

            public Task<TResult> ExecuteAsync<TState, TResult>(
                TState state,
                Func<DbContext, TState, CancellationToken, Task<TResult>> operation,
                Func<DbContext, TState, CancellationToken, Task<ExecutionResult<TResult>>>? verifySucceeded,
                CancellationToken cancellationToken = default) =>
                operation(context, state, cancellationToken);
        }
    }

    [TestClass]
    public class DeploymentDefinitionsAndSnapshotScriptContentTests
    {
        private const string RecordedHash =
            "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

        [TestMethod]
        public void SaveTree_InPlaceEdit_PreservesContentHashWhenPathUnchanged_AndClearsWhenPathChanges()
        {
            var project = new Project { Id = 10, Name = "Payments" };
            var script = new Script
            {
                Id = 5,
                Name = "Deploy",
                Path = "Deploy.ps1",
                PowerShellVersionNumber = "7.4",
                ContentHash = RecordedHash
            };
            var component = new Component
            {
                Id = 20,
                Name = "Deploy",
                Script = script,
                ScriptId = script.Id,
                Projects = new List<Project> { project }
            };
            script.Components = new List<Component> { component };

            var components = new List<Component> { component };
            var componentSet = DbContextMock.GetQueryableMockDbSet(components);
            var context = Substitute.For<IDeploymentContext>();
            context.Components.Returns(componentSet);

            // Non-path metadata edit (NonProdOnly + ComponentName) with unchanged ScriptPath preserves ContentHash.
            DeploymentDefinitions.SaveTree(
                context,
                new[]
                {
                    new ComponentApiModel
                    {
                        ComponentId = 20,
                        ComponentName = "DeployRenamed",
                        ScriptPath = "Deploy.ps1",
                        NonProdOnly = true,
                        PSVersion = "7",
                        IsEnabled = true,
                        ComponentType = ComponentType.PowerShell,
                        Children = new List<ComponentApiModel>()
                    }
                },
                project,
                parent: null,
                kept: new HashSet<int>(),
                canModify: _ => true);

            Assert.AreSame(script, component.Script);
            Assert.AreEqual(RecordedHash, component.Script!.ContentHash);

            // Changing ScriptPath clears ContentHash to null on the in-place Script row.
            DeploymentDefinitions.SaveTree(
                context,
                new[]
                {
                    new ComponentApiModel
                    {
                        ComponentId = 20,
                        ComponentName = "DeployRenamed",
                        ScriptPath = "DeployV2.ps1",
                        NonProdOnly = true,
                        PSVersion = "7",
                        IsEnabled = true,
                        ComponentType = ComponentType.PowerShell,
                        Children = new List<ComponentApiModel>()
                    }
                },
                project,
                parent: null,
                kept: new HashSet<int>(),
                canModify: _ => true);

            Assert.AreSame(script, component.Script);
            Assert.AreEqual("DeployV2.ps1", component.Script!.Path);
            Assert.IsNull(component.Script.ContentHash);
        }

        [TestMethod]
        public void SaveTree_CopyOnWrite_PreservesContentHashOnlyWhenClonedPathEqualsSourcePath()
        {
            var projectA = new Project { Id = 10, Name = "Payments" };
            var projectB = new Project { Id = 20, Name = "Billing" };
            var sharedScript = new Script
            {
                Id = 42,
                Name = "SharedDeploy",
                Path = "SharedDeploy.ps1",
                PowerShellVersionNumber = "7.4",
                ContentHash = RecordedHash
            };
            var componentA = new Component
            {
                Id = 101,
                Name = "PaymentsDeploy",
                Script = sharedScript,
                ScriptId = sharedScript.Id,
                Projects = new List<Project> { projectA }
            };
            var componentB = new Component
            {
                Id = 102,
                Name = "BillingDeploy",
                Script = sharedScript,
                ScriptId = sharedScript.Id,
                Projects = new List<Project> { projectB }
            };
            sharedScript.Components = new List<Component> { componentA, componentB };

            var components = new List<Component> { componentA, componentB };
            var componentSet = DbContextMock.GetQueryableMockDbSet(components);
            var context = Substitute.For<IDeploymentContext>();
            context.Components.Returns(componentSet);

            // COW with SAME path (NonProdOnly changed) copies ContentHash onto the cloned Script.
            DeploymentDefinitions.SaveTree(
                context,
                new[]
                {
                    new ComponentApiModel
                    {
                        ComponentId = 101,
                        ComponentName = "PaymentsDeploy",
                        ScriptPath = "SharedDeploy.ps1",
                        NonProdOnly = true,
                        PSVersion = "7",
                        IsEnabled = true,
                        ComponentType = ComponentType.PowerShell,
                        Children = new List<ComponentApiModel>()
                    }
                },
                projectA,
                parent: null,
                kept: new HashSet<int>(),
                canModify: _ => true);

            Assert.AreNotSame(sharedScript, componentA.Script);
            Assert.AreEqual(RecordedHash, componentA.Script!.ContentHash);
            Assert.AreEqual(RecordedHash, sharedScript.ContentHash);

            // Re-share and test COW with CHANGED path: cloned Script must start with ContentHash = null,
            // while sharedScript keeps RecordedHash.
            componentA.Script = sharedScript;
            DeploymentDefinitions.SaveTree(
                context,
                new[]
                {
                    new ComponentApiModel
                    {
                        ComponentId = 101,
                        ComponentName = "PaymentsDeploy",
                        ScriptPath = "PaymentsCustom.ps1",
                        NonProdOnly = true,
                        PSVersion = "7",
                        IsEnabled = true,
                        ComponentType = ComponentType.PowerShell,
                        Children = new List<ComponentApiModel>()
                    }
                },
                projectA,
                parent: null,
                kept: new HashSet<int>(),
                canModify: _ => true);

            Assert.AreNotSame(sharedScript, componentA.Script);
            Assert.AreEqual("PaymentsCustom.ps1", componentA.Script!.Path);
            Assert.IsNull(componentA.Script.ContentHash);
            Assert.AreEqual(RecordedHash, sharedScript.ContentHash);
            Assert.AreEqual("SharedDeploy.ps1", sharedScript.Path);
        }

        [TestMethod]
        public void ManageProjectsUpdateComponent_PreservesOrClearsContentHashAcrossCowAndInPlaceEdits()
        {
            var sharedScript = new Script
            {
                Id = 50,
                Name = "CompA",
                Path = "Deploy.ps1",
                PowerShellVersionNumber = "7.4",
                ContentHash = RecordedHash
            };
            var project = new Project { Id = 10, Name = "Proj10" };
            var compA = new Component { Id = 1, Name = "CompA", Script = sharedScript, ScriptId = 50, Projects = new List<Project> { project } };
            var compB = new Component { Id = 2, Name = "CompB", Script = sharedScript, ScriptId = 50, Projects = new List<Project> { project } };
            sharedScript.Components = new List<Component> { compA, compB };

            var projects = new List<Project> { project };
            var components = new List<Component> { compA, compB };
            var scripts = new List<Script> { sharedScript };
            var projectSet = DbContextMock.GetQueryableMockDbSet(projects);
            var componentSet = DbContextMock.GetQueryableMockDbSet(components);
            var scriptSet = DbContextMock.GetQueryableMockDbSet(scripts);

            var context = Substitute.For<IDeploymentContext>();
            context.Projects.Returns(projectSet);
            context.Components.Returns(componentSet);
            context.Scripts.Returns(scriptSet);
            var factory = Substitute.For<IDeploymentContextFactory>();
            factory.GetContext().Returns(context);

            var manageSource = new ManageProjectsPersistentSource(
                factory,
                Substitute.For<IRequestsPersistentSource>(),
                Substitute.For<IScriptsAuditPersistentSource>(),
                Substitute.For<ISourceHostAllowList>());

            // COW with same path preserves ContentHash on cloned script
            manageSource.UpdateComponent(
                new ComponentApiModel
                {
                    ComponentId = 1,
                    ComponentName = "CompA",
                    ScriptPath = "Deploy.ps1",
                    NonProdOnly = true,
                    PSVersion = "7",
                    ComponentType = ComponentType.PowerShell
                },
                projectId: 10,
                parentId: null,
                username: @"DOMAIN\alice");

            Assert.AreNotSame(sharedScript, compA.Script);
            Assert.AreEqual(RecordedHash, compA.Script!.ContentHash);

            // In-place path change on compA's now-unshared script clears ContentHash
            compA.Script.Components = new List<Component> { compA };
            manageSource.UpdateComponent(
                new ComponentApiModel
                {
                    ComponentId = 1,
                    ComponentName = "CompA",
                    ScriptPath = "Changed.ps1",
                    NonProdOnly = true,
                    PSVersion = "7",
                    ComponentType = ComponentType.PowerShell
                },
                projectId: 10,
                parentId: null,
                username: @"DOMAIN\alice");

            Assert.IsNull(compA.Script.ContentHash);
            Assert.AreEqual(RecordedHash, sharedScript.ContentHash);
        }

        [TestMethod]
        public void FrozenRequestSnapshot_RoundTripsScriptIdAndContentHash_AndSupportsLegacySnapshots()
        {
            var script = new Script
            {
                Id = 88,
                Name = "Deploy.ps1",
                Path = "Deploy.ps1",
                PowerShellVersionNumber = "7.4",
                ContentHash = RecordedHash
            };

            var snapshotJson = RequestsPersistentSource.SerializeSubmittedScript(
                script,
                isEnabled: true,
                revision: "rev-88");

            var legacyJson = JsonSerializer.Serialize(new
            {
                Id = 89,
                Name = "Legacy.ps1",
                Path = "Legacy.ps1",
                IsPathJSON = false,
                NonProdOnly = false,
                PowerShellVersionNumber = "5.1",
                IsEnabled = true,
                Revision = "rev-89"
            });

            var definitions = new List<DeploymentRequestDefinition>
            {
                new()
                {
                    DeploymentRequestId = 500,
                    Ordinal = 0,
                    ProjectId = 10,
                    ComponentId = 201,
                    ComponentName = "ModernComponent",
                    DefinitionRevision = "rev-88",
                    DefinitionJson = "{}",
                    ProjectJson = "{}",
                    ScriptJson = snapshotJson
                },
                new()
                {
                    DeploymentRequestId = 500,
                    Ordinal = 1,
                    ProjectId = 10,
                    ComponentId = 202,
                    ComponentName = "LegacyComponent",
                    DefinitionRevision = "rev-89",
                    DefinitionJson = "{}",
                    ProjectJson = "{}",
                    ScriptJson = legacyJson
                }
            };

            var definitionSet = DbContextMock.GetQueryableMockDbSet(definitions);
            var context = Substitute.For<IDeploymentContext>();
            context.DeploymentRequestDefinitions.Returns(definitionSet);
            var factory = Substitute.For<IDeploymentContextFactory>();
            factory.GetContext().Returns(context);

            var requests = new RequestsPersistentSource(factory, Substitute.For<IClaimsPrincipalReader>());

            var modernSubmitted = requests.GetSubmittedScript(500, 201);
            Assert.AreEqual(88, modernSubmitted.Id);
            Assert.AreEqual(RecordedHash, modernSubmitted.ContentHash);

            var legacySubmitted = requests.GetSubmittedScript(500, 202);
            Assert.AreEqual(89, legacySubmitted.Id);
            Assert.IsNull(legacySubmitted.ContentHash);
        }

        [TestMethod]
        public void ObserveAndScriptRevision_IncludeNormalizedContentHashInCanonicalIdentity()
        {
            const string secondHash =
                "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

            var storedRevisions = new List<DeploymentDefinitionRevision>();
            var revisionSet = DbContextMock.GetQueryableMockDbSet(storedRevisions);
            var context = Substitute.For<IDeploymentContext>();
            context.DeploymentDefinitionRevisions.Returns(revisionSet);

            var project = new ProjectApiModel { ProjectId = 10, ProjectName = "Payments" };
            var component = new ComponentApiModel
            {
                ComponentId = 201,
                ComponentName = "DeployComponent",
                ScriptPath = "Deploy.ps1",
                ComponentType = ComponentType.PowerShell,
                IsEnabled = true,
                Children = new List<ComponentApiModel>()
            };

            var unsetNull = DeploymentDefinitions.Observe(context, project, component, scriptContentHash: null);
            var unsetWhitespace = DeploymentDefinitions.Observe(context, project, component, scriptContentHash: "   ");
            Assert.AreEqual(unsetNull.Revision, unsetWhitespace.Revision);
            Assert.AreEqual(1, storedRevisions.Count);

            var baselineLower = DeploymentDefinitions.Observe(context, project, component, scriptContentHash: RecordedHash);
            var baselineUpperPadded = DeploymentDefinitions.Observe(
                context,
                project,
                component,
                scriptContentHash: $"  {RecordedHash.ToUpperInvariant()}  ");
            Assert.AreNotEqual(unsetNull.Revision, baselineLower.Revision);
            Assert.AreEqual(baselineLower.Revision, baselineUpperPadded.Revision);
            Assert.AreEqual(2, storedRevisions.Count);

            var promoted = DeploymentDefinitions.Observe(context, project, component, scriptContentHash: secondHash);
            Assert.AreNotEqual(unsetNull.Revision, promoted.Revision);
            Assert.AreNotEqual(baselineLower.Revision, promoted.Revision);
            Assert.AreEqual(3, storedRevisions.Count);

            var script = new Script
            {
                Id = 88,
                Name = "Deploy.ps1",
                Path = "Deploy.ps1",
                PowerShellVersionNumber = "7.4",
                Components = new List<Component>()
            };
            var scriptRevUnset = DeploymentDefinitions.ScriptRevision(script);
            script.ContentHash = RecordedHash;
            var scriptRevRecorded = DeploymentDefinitions.ScriptRevision(script);
            script.ContentHash = $" {RecordedHash.ToUpperInvariant()} ";
            var scriptRevNormalized = DeploymentDefinitions.ScriptRevision(script);

            Assert.AreNotEqual(scriptRevUnset, scriptRevRecorded);
            Assert.AreEqual(scriptRevRecorded, scriptRevNormalized);
        }

        [TestMethod]
        public void Record_ReusesReadScriptContentHashesWithoutExtraComponentQueries_AndBatchesUncapturedDocuments()
        {
            const string secondHash =
                "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

            var project = new Project
            {
                Id = 10,
                Name = "Payments",
                Description = "Before",
                ObjectId = Guid.NewGuid()
            };
            var sharedScript = new Script
            {
                Id = 88,
                Name = "Deploy.ps1",
                Path = "Deploy.ps1",
                PowerShellVersionNumber = "7.4",
                ContentHash = RecordedHash
            };
            var secondScript = new Script
            {
                Id = 89,
                Name = "Verify.ps1",
                Path = "Verify.ps1",
                PowerShellVersionNumber = "7.4",
                ContentHash = secondHash
            };

            var components = new List<Component>
            {
                new()
                {
                    Id = 201,
                    Name = "CompA",
                    ObjectId = Guid.NewGuid(),
                    IsEnabled = true,
                    ScriptId = sharedScript.Id,
                    Script = sharedScript,
                    Projects = new List<Project> { project }
                },
                new()
                {
                    Id = 202,
                    Name = "CompB",
                    ObjectId = Guid.NewGuid(),
                    IsEnabled = true,
                    ScriptId = secondScript.Id,
                    Script = secondScript,
                    Projects = new List<Project> { project }
                }
            };

            var storedChanges = new List<DeploymentDefinitionChange>();
            var storedRevisions = new List<DeploymentDefinitionRevision>();
            var projectSet = DbContextMock.GetQueryableMockDbSet(new List<Project> { project });
            var componentSet = DbContextMock.GetQueryableMockDbSet(components);
            var inputsSet = DbContextMock.GetQueryableMockDbSet(new List<Dorc.PersistentData.Model.CatalogEnvironmentInputs>());
            var changesSet = DbContextMock.GetQueryableMockDbSet(storedChanges);
            var revisionsSet = DbContextMock.GetQueryableMockDbSet(storedRevisions);
            var auditActionsSet = DbContextMock.GetQueryableMockDbSet(new List<Dorc.PersistentData.Model.RefDataAuditAction>());
            var auditsSet = DbContextMock.GetQueryableMockDbSet(new List<RefDataAudit>());

            var context = Substitute.For<IDeploymentContext>();
            var componentQueries = 0;
            context.Projects.Returns(projectSet);
            context.Components.Returns(_ =>
            {
                componentQueries++;
                return componentSet;
            });
            context.CatalogEnvironmentInputs.Returns(inputsSet);
            context.DeploymentDefinitionChanges.Returns(changesSet);
            context.DeploymentDefinitionRevisions.Returns(revisionsSet);
            context.RefDataAuditActions.Returns(auditActionsSet);
            context.RefDataAudits.Returns(auditsSet);

            var before = DeploymentDefinitions.Read(context, project.Id);
            // Mutate tracked Script in-place before reading 'after' to ensure 'before' keeps its immutable snapshot.
            sharedScript.ContentHash = null;
            sharedScript.Path = "Other.ps1";
            var after = DeploymentDefinitions.Read(context, project.Id);

            componentQueries = 0;
            var changeId = DeploymentDefinitions.Record(context, before, after, @"CORP\caller", "Script.SharedEdit");

            Assert.IsNotNull(changeId);
            Assert.AreEqual(0, componentQueries, "Record must reuse Script.ContentHash captured by Read without querying context.Components.");
            var compARevisions = storedRevisions.Where(r => r.ComponentId == 201).ToList();
            Assert.HasCount(2, compARevisions);
            Assert.IsTrue(compARevisions.Any(r => r.DefinitionJson.Contains(RecordedHash, StringComparison.Ordinal)));
            Assert.IsTrue(compARevisions.Any(r => r.DefinitionJson.Contains("\"ContentHash\":null", StringComparison.Ordinal)));

            // Synthetic RefDataApiModel not produced by Read must batch-load all missing component IDs in one query.
            var syntheticAfter = new RefDataApiModel
            {
                Project = after.Project,
                Components = after.Components.Select(c => new ComponentApiModel
                {
                    ComponentId = c.ComponentId,
                    ComponentName = c.ComponentName,
                    ScriptPath = c.ScriptPath,
                    ComponentType = c.ComponentType,
                    IsEnabled = c.IsEnabled
                }).ToList(),
                Revision = "synthetic-rev"
            };

            componentQueries = 0;
            DeploymentDefinitions.Record(context, null, syntheticAfter, @"CORP\caller", "RefData.Update");
            Assert.AreEqual(1, componentQueries, "Uncaptured component IDs must be resolved in a single batch query.");
        }
    }

    [TestClass]
    public class RefDataScriptsControllerContentHashAuthorizationTests
    {
        private const string ValidHash =
            "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

        private IScriptsPersistentSource _scripts = null!;
        private IRolePrivilegesChecker _roles = null!;
        private ISecurityPrivilegesChecker _security = null!;
        private IClaimsPrincipalReader _claims = null!;
        private TestCaptureLogger<RefDataScriptsController> _logger = null!;
        private RefDataScriptsController _controller = null!;

        [TestInitialize]
        public void Setup()
        {
            _scripts = Substitute.For<IScriptsPersistentSource>();
            _roles = Substitute.For<IRolePrivilegesChecker>();
            _security = Substitute.For<ISecurityPrivilegesChecker>();
            _claims = Substitute.For<IClaimsPrincipalReader>();
            _claims.GetUserFullDomainName(Arg.Any<IPrincipal>()).Returns(@"CORP\caller");
            _logger = new TestCaptureLogger<RefDataScriptsController>();

            _controller = new RefDataScriptsController(
                _scripts,
                _roles,
                definitions: null,
                security: _security,
                claims: _claims,
                logger: _logger)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity("TestAuth"))
                    }
                }
            };
        }

        [TestMethod]
        public void Admin_CanRecordAndClearContentHash()
        {
            _roles.IsAdmin(Arg.Any<IPrincipal>()).Returns(true);
            _scripts.RecordContentHash(42, ValidHash, "Deploy.ps1", Arg.Any<IPrincipal>(), null).Returns(true);
            _scripts.RecordContentHash(42, null, null, Arg.Any<IPrincipal>(), null).Returns(true);

            var recordResponse = (ObjectResult)_controller.PutContentHash(
                42,
                new ScriptContentHashApiModel
                {
                    ContentHash = ValidHash.ToUpperInvariant(),
                    ExpectedPath = "Deploy.ps1"
                });
            Assert.AreEqual(StatusCodes.Status200OK, recordResponse.StatusCode);
            Assert.AreEqual(true, recordResponse.Value);

            var clearResponse = (ObjectResult)_controller.PutContentHash(
                42, new ScriptContentHashApiModel { ContentHash = null });
            Assert.AreEqual(StatusCodes.Status200OK, clearResponse.StatusCode);
            Assert.AreEqual(true, clearResponse.Value);
        }

        [TestMethod]
        public void ProjectOwner_CanRecordValidHashOnUnsharedProjectScript_ButCannotClearBaseline()
        {
            _roles.IsAdmin(Arg.Any<IPrincipal>()).Returns(false);
            _security.CanModifyProject(Arg.Any<ClaimsPrincipal>(), 10).Returns(true);
            _scripts.RecordContentHash(
                    42,
                    ValidHash,
                    null,
                    Arg.Any<IPrincipal>(),
                    Arg.Any<Func<int, bool>?>())
                .Returns(call =>
                {
                    var authorize = call.ArgAt<Func<int, bool>?>(4);
                    Assert.IsNotNull(authorize);
                    Assert.IsTrue(authorize(10));
                    return true;
                });

            var recordResponse = (ObjectResult)_controller.PutContentHash(
                42, new ScriptContentHashApiModel { ContentHash = ValidHash });
            Assert.AreEqual(StatusCodes.Status200OK, recordResponse.StatusCode);

            var clearResponse = (ObjectResult)_controller.PutContentHash(
                42, new ScriptContentHashApiModel { ContentHash = "" });
            Assert.AreEqual(StatusCodes.Status403Forbidden, clearResponse.StatusCode);
            _scripts.DidNotReceive().RecordContentHash(
                42,
                Arg.Is<string?>(h => h == null),
                Arg.Any<string?>(),
                Arg.Any<IPrincipal>(),
                Arg.Any<Func<int, bool>?>());
        }

        [TestMethod]
        public void SingleProjectOwner_IsBlockedFromMutatingSharedScriptReferencedByAnotherProject()
        {
            _roles.IsAdmin(Arg.Any<IPrincipal>()).Returns(false);
            _security.CanModifyProject(Arg.Any<ClaimsPrincipal>(), 10).Returns(true);
            _security.CanModifyProject(Arg.Any<ClaimsPrincipal>(), 20).Returns(false);
            _scripts.RecordContentHash(
                    42,
                    ValidHash,
                    null,
                    Arg.Any<IPrincipal>(),
                    Arg.Any<Func<int, bool>?>())
                .Returns(call =>
                {
                    var authorize = call.ArgAt<Func<int, bool>?>(4)!;
                    if (!authorize(10) || !authorize(20))
                    {
                        throw new UnauthorizedAccessException();
                    }

                    return true;
                });

            var response = (ObjectResult)_controller.PutContentHash(
                42, new ScriptContentHashApiModel { ContentHash = ValidHash });

            Assert.AreEqual(StatusCodes.Status403Forbidden, response.StatusCode);
        }

        [TestMethod]
        public void MultiProjectOwner_WithWriteRightsOnEveryReferencingProject_CanRecordHash()
        {
            _roles.IsAdmin(Arg.Any<IPrincipal>()).Returns(false);
            _security.CanModifyProject(Arg.Any<ClaimsPrincipal>(), 10).Returns(true);
            _security.CanModifyProject(Arg.Any<ClaimsPrincipal>(), 20).Returns(true);
            _scripts.RecordContentHash(
                    42,
                    ValidHash,
                    "Deploy.ps1",
                    Arg.Any<IPrincipal>(),
                    Arg.Any<Func<int, bool>?>())
                .Returns(call =>
                {
                    var authorize = call.ArgAt<Func<int, bool>?>(4)!;
                    Assert.IsTrue(authorize(10) && authorize(20));
                    return true;
                });

            var response = (ObjectResult)_controller.PutContentHash(
                42,
                new ScriptContentHashApiModel
                {
                    ContentHash = ValidHash,
                    ExpectedPath = "Deploy.ps1"
                });

            Assert.AreEqual(StatusCodes.Status200OK, response.StatusCode);
            _scripts.Received(1).RecordContentHash(
                42,
                ValidHash,
                "Deploy.ps1",
                Arg.Any<IPrincipal>(),
                Arg.Any<Func<int, bool>?>());
        }

        [TestMethod]
        public void NonAdmin_IsBlockedFromMutatingUnreferencedOrNonexistentScript()
        {
            _roles.IsAdmin(Arg.Any<IPrincipal>()).Returns(false);
            _scripts.RecordContentHash(
                    Arg.Any<int>(),
                    ValidHash,
                    null,
                    Arg.Any<IPrincipal>(),
                    Arg.Any<Func<int, bool>?>())
                .Returns(call =>
                {
                    Assert.IsNotNull(call.ArgAt<Func<int, bool>?>(4));
                    throw new UnauthorizedAccessException();
                });

            var orphanResponse = (ObjectResult)_controller.PutContentHash(
                42, new ScriptContentHashApiModel { ContentHash = ValidHash });
            Assert.AreEqual(StatusCodes.Status403Forbidden, orphanResponse.StatusCode);

            var missingResponse = (ObjectResult)_controller.PutContentHash(
                999, new ScriptContentHashApiModel { ContentHash = ValidHash });
            Assert.AreEqual(StatusCodes.Status403Forbidden, missingResponse.StatusCode);
        }

        [TestMethod]
        public void ExpectedPathMismatch_Returns409ConflictAndLogsSingleLineText()
        {
            _roles.IsAdmin(Arg.Any<IPrincipal>()).Returns(true);
            _claims.GetUserFullDomainName(Arg.Any<IPrincipal>()).Returns("CORP\\promoter\r\nFORGED");
            _scripts.RecordContentHash(
                    42,
                    ValidHash,
                    "Old.ps1\r\nINJECTED",
                    Arg.Any<IPrincipal>(),
                    null)
                .Returns(_ => throw new InvalidOperationException(
                    "The script path does not match the expected path for this content hash baseline."));

            var response = (ObjectResult)_controller.PutContentHash(
                42,
                new ScriptContentHashApiModel
                {
                    ContentHash = ValidHash,
                    ExpectedPath = "Old.ps1\r\nINJECTED"
                });

            Assert.AreEqual(StatusCodes.Status409Conflict, response.StatusCode);
            Assert.IsTrue(_logger.Entries.Count > 0);
            foreach (var message in _logger.Entries)
            {
                Assert.IsFalse(message.Contains('\r'));
                Assert.IsFalse(message.Contains('\n'));
            }
        }

        [TestMethod]
        public void MalformedHashAndControlCharacters_ReturnBadRequestAndLogSingleLineText()
        {
            _roles.IsAdmin(Arg.Any<IPrincipal>()).Returns(true);
            _claims.GetUserFullDomainName(Arg.Any<IPrincipal>()).Returns("CORP\\mallory\r\nFORGED");

            var response = (ObjectResult)_controller.PutContentHash(
                42, new ScriptContentHashApiModel { ContentHash = "bad-hash\r\nINJECTED" });

            Assert.AreEqual(StatusCodes.Status400BadRequest, response.StatusCode);
            _scripts.DidNotReceiveWithAnyArgs().RecordContentHash(default, default, default, default!, default);
            _scripts.DidNotReceiveWithAnyArgs().RecordContentHash(default, default, default!);
            Assert.IsTrue(_logger.Entries.Count > 0);
            foreach (var message in _logger.Entries)
            {
                Assert.IsFalse(message.Contains('\r'));
                Assert.IsFalse(message.Contains('\n'));
            }
        }
    }

    internal sealed class TestCaptureLogger<T> : ILogger<T>
    {
        public List<string> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(formatter(state, exception));
        }
    }

    /// <summary>
    /// The hash is computed in the shared model assembly precisely so that the API that records
    /// one and the two PowerShell runners that verify against it cannot disagree — they share no
    /// other code, and they compile for different frameworks.
    /// </summary>
    [TestClass]
    public class ScriptContentHashTests
    {
        [TestMethod]
        public void HashesTheKnownVectorForAbc()
        {
            Assert.AreEqual(
                "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                ScriptContentHash.ComputeFromScriptText("abc"));
        }

        [TestMethod]
        public void BomAndBomlessUtf8ScriptFilesProduceTheSameCanonicalHash()
        {
            var withoutBomPath = Path.Join(
                AppContext.BaseDirectory,
                $"dorc-nobom-{Guid.NewGuid():N}.ps1");
            var withBomPath = Path.Join(
                AppContext.BaseDirectory,
                $"dorc-bom-{Guid.NewGuid():N}.ps1");

            try
            {
                File.WriteAllText(withoutBomPath, "Write-Host 'x'", new System.Text.UTF8Encoding(false));
                File.WriteAllText(withBomPath, "Write-Host 'x'", new System.Text.UTF8Encoding(true));

                Assert.AreEqual(
                    ScriptContentHash.ComputeFromFile(withoutBomPath),
                    ScriptContentHash.ComputeFromFile(withBomPath));
            }
            finally
            {
                if (File.Exists(withoutBomPath)) File.Delete(withoutBomPath);
                if (File.Exists(withBomPath)) File.Delete(withBomPath);
            }
        }

        [TestMethod]
        public void MatchesIgnoringCaseAndSurroundingSpace()
        {
            var hash = ScriptContentHash.ComputeFromScriptText("abc");

            Assert.IsTrue(ScriptContentHash.Matches($"  {hash.ToUpperInvariant()} ", hash));
        }

        /// <summary>
        /// An absent baseline is not a match. It is the unrecorded case, which the caller has to
        /// handle as its own outcome rather than as a passed or failed verification.
        /// </summary>
        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        public void AnAbsentBaselineNeverMatches(string? recorded)
        {
            Assert.IsFalse(ScriptContentHash.Matches(
                recorded!, ScriptContentHash.ComputeFromScriptText("abc")));
        }
    }
}
