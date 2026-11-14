using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core;
using Dorc.Core.Configuration;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Core.Security;
using Dorc.Core.VariableResolution;
using Dorc.Monitor.Notifications;
using Dorc.Monitor.Pipes;
using Dorc.Monitor.RequestProcessors;
using Dorc.Monitor.RunnerProcess;
using Dorc.Monitor.Security;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Security;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Dorc.Monitor.Tests
{
    /// <summary>
    /// Every local artefact of a deployment - the script group bundle, the Terraform working
    /// directory, and the Terraform plan staged beside it - is derived from resolved
    /// deployment properties, and every one of them was created under %ProgramData% with an
    /// inherited ACL that admits any authenticated user on the host.
    ///
    /// These cover the directory restriction itself. The Terraform plan directory, which
    /// became per-deployment and expiring in S-021, is covered by TerraformPlanLifetimeTests.
    /// </summary>
    [TestClass]
    [SupportedOSPlatform("windows")]
    public class DeploymentArtefactDirectoryTests
    {
        private string _scratch = null!;

        [TestInitialize]
        public void Setup()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Inconclusive("Windows ACLs are the subject of these tests.");
            }

            // Under the test output rather than the machine's temp directory, so a failed run
            // leaves its evidence with the run that produced it.
            _scratch = Path.Join(AppContext.BaseDirectory, "artefact-acl-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_scratch);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (_scratch is not null && Directory.Exists(_scratch))
            {
                TerraformPlanLifetimeTests.RemoveScratchTreeSafely(_scratch);
            }
        }

        private static AuthorizationRuleCollection RulesOf(string path) =>
            new DirectoryInfo(path)
                .GetAccessControl()
                .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));

        private static bool IsProtected(string path) =>
            new DirectoryInfo(path).GetAccessControl().AreAccessRulesProtected;

        private static IEnumerable<FileSystemAccessRule> RulesFor(string path, SecurityIdentifier sid) =>
            RulesOf(path).Cast<FileSystemAccessRule>().Where(rule => rule.IdentityReference.Equals(sid));

        private static SecurityIdentifier Everyone => new(WellKnownSidType.WorldSid, null);

        private static SecurityIdentifier OwnerRights => new("S-1-3-4");

        private static SecurityIdentifier CurrentUser => WindowsIdentity.GetCurrent().User!;

        private static string CurrentAccountName => WindowsIdentity.GetCurrent().Name;

        [TestMethod]
        public void ARestrictedDirectoryInheritsNothingFromProgramData()
        {
            var path = Path.Join(_scratch, "created");

            RestrictedDirectory.Ensure(path);

            Assert.IsTrue(Directory.Exists(path));
            Assert.IsTrue(IsProtected(path), "The DACL must be protected, or %ProgramData%'s Users ACE is inherited.");
            Assert.IsFalse(RulesOf(path).Cast<FileSystemAccessRule>().Any(rule => rule.IsInherited));
            Assert.IsEmpty(RulesFor(path, Everyone));
        }

        /// <summary>
        /// The artefacts are what actually has to be unreadable; the directory is only the
        /// means. Without inheritable ACEs a file created inside it carries no restriction of
        /// its own.
        /// </summary>
        [TestMethod]
        public void TheRestrictionIsInheritedByTheArtefactsWrittenIntoIt()
        {
            var path = Path.Join(_scratch, "inheritable");

            RestrictedDirectory.Ensure(path);

            var writer = RulesFor(path, CurrentUser).Single();
            Assert.AreEqual(
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                writer.InheritanceFlags);
            Assert.AreEqual(FileSystemRights.FullControl, writer.FileSystemRights & FileSystemRights.FullControl);

            var artefact = Path.Join(path, "artefact.json");
            File.WriteAllText(artefact, "{}");

            var artefactRules = new FileInfo(artefact)
                .GetAccessControl()
                .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .ToList();
            Assert.IsEmpty(artefactRules.Where(rule => rule.IdentityReference.Equals(Everyone)));
            Assert.IsNotEmpty(artefactRules.Where(rule => rule.IdentityReference.Equals(CurrentUser)));
        }

        /// <summary>
        /// A directory an administrator deleted and re-created by hand is back to inheriting
        /// %ProgramData%. Applying the restriction only at creation would leave every artefact
        /// written afterwards readable by the host.
        /// </summary>
        [TestMethod]
        public void AnExistingDirectoryIsRestrictedAgainRatherThanLeftAsFound()
        {
            var path = Path.Join(_scratch, "loosened");
            Directory.CreateDirectory(path);

            var loosened = new DirectoryInfo(path).GetAccessControl();
            loosened.SetAccessRuleProtection(isProtected: false, preserveInheritance: true);
            loosened.AddAccessRule(new FileSystemAccessRule(
                Everyone,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                System.Security.AccessControl.AccessControlType.Allow));
            new DirectoryInfo(path).SetAccessControl(loosened);
            Assert.IsNotEmpty(RulesFor(path, Everyone), "The test has not reproduced the exposure it is about.");

            RestrictedDirectory.Ensure(path);

            Assert.IsTrue(IsProtected(path));
            Assert.IsEmpty(RulesFor(path, Everyone));
        }

        /// <summary>
        /// The Runner writes the plan as the deployment account, so that account has to be
        /// admitted - but only far enough to write its artefacts, not far enough to re-open the
        /// directory to the host by rewriting the ACL.
        /// </summary>
        [TestMethod]
        public void AnAdmittedDeploymentAccountGetsModifyAndNotOwnershipOfTheAcl()
        {
            var path = Path.Join(_scratch, "admitted");
            var deploymentAccount = new SecurityIdentifier(WellKnownSidType.NetworkServiceSid, null);

            RestrictedDirectory.Ensure(path, deploymentAccount);

            var granted = RulesFor(path, deploymentAccount).Single();
            Assert.AreEqual(FileSystemRights.Modify, granted.FileSystemRights & FileSystemRights.Modify);
            Assert.AreNotEqual(
                FileSystemRights.ChangePermissions,
                granted.FileSystemRights & FileSystemRights.ChangePermissions);
            Assert.AreNotEqual(
                FileSystemRights.TakeOwnership,
                granted.FileSystemRights & FileSystemRights.TakeOwnership);
        }

        [TestMethod]
        public void ARestrictedDirectoryRefusesToFollowAJunctionOntoItsTarget()
        {
            var target = Path.Join(_scratch, "target");
            Directory.CreateDirectory(target);
            var targetSecurity = new DirectoryInfo(target).GetAccessControl();
            targetSecurity.AddAccessRule(new FileSystemAccessRule(
                Everyone,
                FileSystemRights.ReadAndExecute,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                System.Security.AccessControl.AccessControlType.Allow));
            new DirectoryInfo(target).SetAccessControl(targetSecurity);

            var junction = Path.Join(_scratch, "junction");
            TerraformPlanLifetimeTests.CreateDirectoryLink(junction, target);

            Assert.ThrowsExactly<IOException>(() => RestrictedDirectory.Ensure(junction));
            Assert.IsNotEmpty(
                RulesFor(target, Everyone),
                "RestrictedDirectory.Ensure must never rewrite the DACL of a junction's target.");
        }

        [TestMethod]
        public void CreateNewCreatesRestrictedDirectoryAndRefusesPreExistingDirectoryOrJunction()
        {
            var fresh = Path.Join(_scratch, "fresh-only");
            RestrictedDirectory.CreateNew(fresh);

            Assert.IsTrue(Directory.Exists(fresh));
            Assert.IsTrue(IsProtected(fresh));
            Assert.IsEmpty(RulesFor(fresh, Everyone));

            // Calling CreateNew again on an existing directory must fail closed.
            Assert.ThrowsExactly<IOException>(() => RestrictedDirectory.CreateNew(fresh));

            // Calling CreateNew on a pre-existing junction must fail closed without touching target ACLs.
            var target = Path.Join(_scratch, "createnew-target");
            Directory.CreateDirectory(target);
            var targetSecurity = new DirectoryInfo(target).GetAccessControl();
            targetSecurity.AddAccessRule(new FileSystemAccessRule(
                Everyone,
                FileSystemRights.ReadAndExecute,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                System.Security.AccessControl.AccessControlType.Allow));
            new DirectoryInfo(target).SetAccessControl(targetSecurity);

            var junction = Path.Join(_scratch, "createnew-junction");
            TerraformPlanLifetimeTests.CreateDirectoryLink(junction, target);

            Assert.ThrowsExactly<IOException>(() => RestrictedDirectory.CreateNew(junction));
            Assert.IsNotEmpty(RulesFor(target, Everyone));
        }

        [TestMethod]
        public void OwnerRightsAceGrantsReadPermissionsOnlyAndNotChangePermissions()
        {
            var path = Path.Join(_scratch, "owner-rights-dir");
            var deploymentAccount = new SecurityIdentifier(WellKnownSidType.NetworkServiceSid, null);

            RestrictedDirectory.Ensure(path, deploymentAccount);

            var dirOwnerRights = RulesFor(path, OwnerRights).Single();
            Assert.AreEqual(System.Security.AccessControl.AccessControlType.Allow, dirOwnerRights.AccessControlType);
            Assert.AreEqual(
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                dirOwnerRights.InheritanceFlags);
            Assert.AreEqual(
                FileSystemRights.ReadPermissions,
                dirOwnerRights.FileSystemRights & FileSystemRights.ReadPermissions);
            Assert.AreEqual(
                (FileSystemRights)0,
                dirOwnerRights.FileSystemRights & FileSystemRights.ChangePermissions,
                "S-1-3-4 must not grant ChangePermissions (WRITE_DAC), so a Runner-owned child cannot rewrite its own ACL.");
            Assert.AreEqual(
                (FileSystemRights)0,
                dirOwnerRights.FileSystemRights & FileSystemRights.TakeOwnership);

            var filePath = Path.Join(path, "staged.tfplan");
            File.WriteAllText(filePath, "plan");
            var fileSecurity = RestrictedDirectory.BuildFileSecurity([deploymentAccount]);
            new FileInfo(filePath).SetAccessControl(fileSecurity);

            var fileOwnerRights = new FileInfo(filePath)
                .GetAccessControl()
                .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .Single(rule => rule.IdentityReference.Equals(OwnerRights));
            Assert.AreEqual(InheritanceFlags.None, fileOwnerRights.InheritanceFlags);
            Assert.AreEqual(
                FileSystemRights.ReadPermissions,
                fileOwnerRights.FileSystemRights & FileSystemRights.ReadPermissions);
            Assert.AreEqual(
                (FileSystemRights)0,
                fileOwnerRights.FileSystemRights & FileSystemRights.ChangePermissions);
        }

        private static SecurityIdentifier ReadActualOwner(string path)
        {
            using var handle = ReparseFreeHandles.OpenVerifiedDirectoryHandle(
                path,
                ReparseFreeHandles.FileReadAttributes | ReparseFreeHandles.ReadControl);
            return ReparseFreeHandles.GetOwnerSid(handle, path);
        }

        private static SecurityIdentifier[] OwnersExcluding(SecurityIdentifier untrustedOwner)
        {
            var localSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var localService = new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null);
            return [untrustedOwner.Equals(localSystem) ? localService : localSystem];
        }

        [TestMethod]
        public void EnsureRefusesUntrustedDirectoryOwnerRatherThanAdoptingInPlace()
        {
            var squatted = Path.Join(_scratch, "squatted-owner");
            Directory.CreateDirectory(squatted);

            // Even when the squatter grants TakeOwnership + ChangePermissions (hoping Monitor
            // will adopt the directory in-place while the squatter retains a pre-opened
            // WRITE_DAC handle), EnsureWithOwners must fail closed with IOException.
            var squattedAcl = new DirectoryInfo(squatted).GetAccessControl();
            squattedAcl.AddAccessRule(new FileSystemAccessRule(
                CurrentUser,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                System.Security.AccessControl.AccessControlType.Allow));
            new DirectoryInfo(squatted).SetAccessControl(squattedAcl);

            var actualOwner = ReadActualOwner(squatted);
            var privilegedExcludingSquatter = OwnersExcluding(actualOwner);

            var refusal = Assert.ThrowsExactly<IOException>(() =>
                RestrictedDirectory.EnsureWithOwners(squatted, privilegedExcludingSquatter));
            StringAssert.Contains(refusal.Message, "not a privileged account");

            // A directory already owned by a privileged principal continues to be hardened cleanly.
            var privilegedOwned = Path.Join(_scratch, "privileged-owner");
            Directory.CreateDirectory(privilegedOwned);
            var privilegedAcl = new DirectoryInfo(privilegedOwned).GetAccessControl();
            privilegedAcl.AddAccessRule(new FileSystemAccessRule(
                Everyone,
                FileSystemRights.ReadAndExecute,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                System.Security.AccessControl.AccessControlType.Allow));
            new DirectoryInfo(privilegedOwned).SetAccessControl(privilegedAcl);

            RestrictedDirectory.EnsureWithOwners(privilegedOwned, [ReadActualOwner(privilegedOwned)]);

            Assert.IsTrue(IsProtected(privilegedOwned));
            Assert.IsEmpty(RulesFor(privilegedOwned, Everyone));

            // CreateNewDirectoryWithSecurity also verifies privileged ownership on the pinned handle itself.
            var newDirWithUntrustedOwner = Path.Join(_scratch, "createnew-untrusted-owner");
            var createRefusal = Assert.ThrowsExactly<IOException>(() =>
                ReparseFreeHandles.CreateNewDirectoryWithSecurity(
                    newDirWithUntrustedOwner,
                    RestrictedDirectory.BuildSecurity([]),
                    privilegedExcludingSquatter,
                    out _,
                    out _));
            StringAssert.Contains(createRefusal.Message, "not a privileged account");
        }

        [TestMethod]
        public void EnsureProgramDataRootEnforcesNonInheritingCreateDirectoriesAndVerifiesParentIdentity()
        {
            var programDataDorc = Path.Join(_scratch, "programdata-dorc");
            RestrictedDirectory.EnsureProgramDataRoot(programDataDorc);

            Assert.IsTrue(IsProtected(programDataDorc));
            var authenticatedUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            var authRule = RulesFor(programDataDorc, authenticatedUsers).Single();
            Assert.AreEqual(InheritanceFlags.None, authRule.InheritanceFlags);
            Assert.AreEqual(RestrictedDirectory.ProgramDataRootAuthenticatedUsersRights, authRule.FileSystemRights);
            Assert.AreEqual((FileSystemRights)0, authRule.FileSystemRights & FileSystemRights.DeleteSubdirectoriesAndFiles);
            Assert.AreEqual((FileSystemRights)0, authRule.FileSystemRights & FileSystemRights.Delete);
            Assert.AreEqual((FileSystemRights)0, authRule.FileSystemRights & FileSystemRights.ChangePermissions);
            Assert.AreEqual((FileSystemRights)0, authRule.FileSystemRights & FileSystemRights.TakeOwnership);

            // EnsureProgramDataRoot immediately pre-creates terraform-plans and artifacts as
            // privileged-owned restricted directories so Authenticated Users cannot squat them
            // via CreateDirectories.
            var preCreatedPlansRoot = Path.Join(programDataDorc, Dorc.Monitor.Terraform.TerraformPlanStore.DirectoryName);
            Assert.IsTrue(Directory.Exists(preCreatedPlansRoot));
            Assert.IsTrue(IsProtected(preCreatedPlansRoot));
            Assert.IsEmpty(RulesFor(preCreatedPlansRoot, authenticatedUsers));
            Assert.IsEmpty(RulesFor(preCreatedPlansRoot, Everyone));

            var preCreatedArtifactsRoot = Path.Join(programDataDorc, RestrictedDirectory.ArtifactsDirectoryName);
            Assert.IsTrue(Directory.Exists(preCreatedArtifactsRoot));
            Assert.IsTrue(IsProtected(preCreatedArtifactsRoot));
            Assert.IsEmpty(RulesFor(preCreatedArtifactsRoot, authenticatedUsers));
            Assert.IsEmpty(RulesFor(preCreatedArtifactsRoot, Everyone));

            // CreateNew refuses to create a child directory when the parent's NTFS FileIdentity does not match.
            var bogusParentIdentity = new FileIdentity(0xDEADBEEFUL, 0x1234UL, 0UL);
            var rejectedChild = Path.Join(programDataDorc, "should-not-exist");
            Assert.ThrowsExactly<IOException>(() =>
                RestrictedDirectory.CreateNew(
                    rejectedChild,
                    bogusParentIdentity,
                    out _,
                    out _));
            Assert.IsFalse(Directory.Exists(rejectedChild));

            var untrustedProgramData = Path.Join(_scratch, "untrusted-programdata-dorc");
            Directory.CreateDirectory(untrustedProgramData);
            var privilegedExcludingCreator = OwnersExcluding(ReadActualOwner(untrustedProgramData));
            var rootRefusal = Assert.ThrowsExactly<IOException>(() =>
                RestrictedDirectory.EnsureProgramDataRootWithOwners(
                    untrustedProgramData,
                    privilegedExcludingCreator,
                    out _,
                    out _));
            StringAssert.Contains(rootRefusal.Message, "not a privileged account");
        }

        [TestMethod]
        public void PendingRequestProcessorGrantsPerRequestGitHubArtifactDropReadAndExecuteToExecutionIdentityWhileKeepingProgramDataRestricted()
        {
            var programDataDorc = Path.Join(_scratch, "programdata-dorc-artifacts");
            RestrictedDirectory.EnsureProgramDataRoot(programDataDorc);

            var artifactsRoot = Path.Join(programDataDorc, RestrictedDirectory.ArtifactsDirectoryName);
            var siblingDrop = Path.Join(artifactsRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(siblingDrop);
            File.WriteAllText(Path.Join(siblingDrop, "other-secret.ps1"), "Write-Host 'other'");
            RestrictedDirectory.RestrictReadTree(siblingDrop, []);

            var requestDrop = Path.Join(artifactsRoot, Guid.NewGuid().ToString("N"));
            var extractedDropDir = Path.Join(requestDrop, "drop");
            Directory.CreateDirectory(extractedDropDir);
            var extractedScript = Path.Join(extractedDropDir, "deploy.ps1");
            File.WriteAllText(extractedScript, "Write-Host 'hello'");

            var localServiceSid = new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null);
            var builtinUsersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            var authenticatedUsersSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);

            const string artifactUrl = "https://api.github.com/repos/sefe/dorc/actions/artifacts/777/zip";
            var downloader = Substitute.For<IGitHubArtifactDownloader>();
            downloader.IsGitHubArtifactUrl(artifactUrl).Returns(true);
            downloader.DownloadAndExtract(artifactUrl).Returns(requestDrop);
            downloader.When(d => d.Cleanup(requestDrop)).Do(_ =>
            {
                if (Directory.Exists(requestDrop))
                {
                    Directory.Delete(requestDrop, recursive: true);
                }
            });

            var credentialSource = Substitute.For<IDeploymentCredentialSource>();
            credentialSource.Resolve(DeploymentTier.NonProduction, "nonprod-runner")
                .Returns(_ => DeploymentCredential.FromPlainText("LOCAL SERVICE", "unused"));

            var configSettings = Substitute.For<IConfigurationSettings>();
            configSettings.GetConfigurationDomainNameIntra().Returns("NT AUTHORITY");

            var loggerFactory = Substitute.For<ILoggerFactory>();
            loggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());

            var requestsPds = Substitute.For<IRequestsPersistentSource>();
            requestsPds.SetRequestStartStatus(
                Arg.Any<DeploymentRequestApiModel>(),
                Arg.Any<DeploymentRequestStatus>(),
                Arg.Any<DateTimeOffset>()).Returns(true);
            var component = new ComponentApiModel { ComponentId = 1, ComponentName = "DeployComp", IsEnabled = true };
            requestsPds.GetSubmittedComponents(88).Returns(new List<ComponentApiModel> { component });
            requestsPds.GetDeploymentResultsForRequest(88).Returns(new List<DeploymentResultApiModel>
            {
                new() { Id = 1, ComponentId = 1, RequestId = 88, Status = DeploymentResultStatus.Pending.ToString() }
            });

            var envsPds = Substitute.For<IEnvironmentsPersistentSource>();
            envsPds.GetEnvironment("DevEnv").Returns(new EnvironmentApiModel
            {
                EnvironmentId = 10,
                EnvironmentName = "DevEnv",
                ExecutionIdentityReference = "nonprod-runner"
            });

            var configValuesPds = Substitute.For<IConfigValuesPersistentSource>();
            configValuesPds.GetConfigValue("ScriptRoot").Returns(@"C:\Scripts");
            configValuesPds.GetConfigValue("ScriptRoot", Arg.Any<string>()).Returns(@"C:\Scripts");
            configValuesPds.GetConfigValue("DeploymentLogDir", Arg.Any<string>()).Returns(@"C:\Logs");
            configValuesPds.GetAllConfigValues(true).Returns(Array.Empty<ConfigValueApiModel>());

            var propValuesPds = Substitute.For<IPropertyValuesPersistentSource>();
            propValuesPds.LoadAllPropertiesIntoCache().Returns(new Dictionary<string, PropertyValueDto>());

            var eventsPublisher = Substitute.For<IDeploymentEventsPublisher>();
            eventsPublisher.PublishRequestStatusChangedAsync(Arg.Any<DeploymentRequestEventData>()).Returns(Task.CompletedTask);
            eventsPublisher.PublishResultStatusChangedAsync(Arg.Any<DeploymentResultEventData>()).Returns(Task.CompletedTask);

            var inspectedDuringDeploy = false;
            Exception? callbackFailure = null;
            var componentProcessor = Substitute.For<IComponentProcessor>();
            componentProcessor.DeployComponent(
                component,
                Arg.Any<DeploymentResultApiModel>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<RequestExecutionIdentityAdoption>(),
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, VariableValue>>(),
                Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    try
                    {
                        downloader.Received(1).DownloadAndExtract(artifactUrl);
                        Assert.AreEqual("nonprod-runner", call.ArgAt<string?>(7));

                        // Per-request drop root and extracted children grant ReadAndExecute (with CI|OI on directories)
                        // to the resolved non-admin execution identity without granting Write/Modify/Delete.
                        foreach (var dir in new[] { requestDrop, extractedDropDir })
                        {
                            Assert.IsTrue(IsProtected(dir), $"'{dir}' must carry a protected DACL.");
                            var runnerRule = RulesFor(dir, localServiceSid).Single();
                            Assert.AreEqual(
                                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                                runnerRule.InheritanceFlags);
                            Assert.AreEqual(
                                FileSystemRights.ReadAndExecute,
                                runnerRule.FileSystemRights & FileSystemRights.ReadAndExecute);
                            Assert.AreEqual((FileSystemRights)0, runnerRule.FileSystemRights & FileSystemRights.Write);
                            Assert.AreEqual((FileSystemRights)0, runnerRule.FileSystemRights & FileSystemRights.Delete);
                            Assert.AreEqual((FileSystemRights)0, runnerRule.FileSystemRights & FileSystemRights.ChangePermissions);
                            Assert.IsEmpty(RulesFor(dir, builtinUsersSid));
                            Assert.IsEmpty(RulesFor(dir, authenticatedUsersSid));
                        }

                        var fileAcl = new FileInfo(extractedScript).GetAccessControl();
                        Assert.IsTrue(fileAcl.AreAccessRulesProtected);
                        var fileRunnerRules = fileAcl
                            .GetAccessRules(true, true, typeof(SecurityIdentifier))
                            .Cast<FileSystemAccessRule>()
                            .Where(r => localServiceSid.Equals(r.IdentityReference))
                            .ToList();
                        Assert.HasCount(1, fileRunnerRules);
                        Assert.AreEqual(
                            FileSystemRights.ReadAndExecute,
                            fileRunnerRules[0].FileSystemRights & FileSystemRights.ReadAndExecute);
                        Assert.AreEqual((FileSystemRights)0, fileRunnerRules[0].FileSystemRights & FileSystemRights.Write);

                        // %ProgramData%\dorc, artifacts root, and sibling drops remain closed to this runner.
                        Assert.IsEmpty(RulesFor(programDataDorc, localServiceSid));
                        Assert.IsEmpty(RulesFor(programDataDorc, builtinUsersSid));
                        Assert.IsEmpty(RulesFor(artifactsRoot, localServiceSid));
                        Assert.IsEmpty(RulesFor(artifactsRoot, builtinUsersSid));
                        Assert.IsEmpty(RulesFor(artifactsRoot, authenticatedUsersSid));
                        Assert.IsEmpty(RulesFor(siblingDrop, localServiceSid));

                        inspectedDuringDeploy = true;
                        return true;
                    }
                    catch (Exception ex)
                    {
                        callbackFailure = ex;
                        throw;
                    }
                });

            var processor = new PendingRequestProcessor(
                loggerFactory,
                componentProcessor,
                Substitute.For<IVariableScopeOptionsResolver>(),
                requestsPds,
                propValuesPds,
                envsPds,
                Substitute.For<IManageProjectsPersistentSource>(),
                configValuesPds,
                Substitute.For<IPropertyEvaluator>(),
                eventsPublisher,
                Substitute.For<IDeploymentNotificationSink>(),
                downloader,
                Substitute.For<ISourceHostAllowList>(),
                new ScriptScopeConfigValues(new ConfigurationBuilder().Build()),
                credentialSource,
                configSettings);

            var dto = new RequestToProcessDto(
                new DeploymentRequestApiModel
                {
                    Id = 88,
                    EnvironmentName = "DevEnv",
                    IsProd = false,
                    BuildNumber = "1.2.3",
                    RequestDetails = "<DeploymentRequestDetail />"
                },
                new DeploymentRequestDetail
                {
                    EnvironmentName = "DevEnv",
                    Components = new List<string> { "DeployComp" },
                    BuildDetail = new BuildDetail { DropLocation = artifactUrl }
                });

            processor.Execute(dto, CancellationToken.None, loggerFactory);

            if (callbackFailure is not null)
            {
                ExceptionDispatchInfo.Capture(callbackFailure).Throw();
            }

            Assert.IsTrue(inspectedDuringDeploy, "DeployComponent must have executed and verified DropFolder ACLs.");
            Assert.IsFalse(Directory.Exists(requestDrop), "Per-request GitHub artifact directory must be cleaned up after Execute.");
        }

        [TestMethod]
        public void PendingRequestProcessorCleansUpExtractedGitHubArtifactDropWhenPostExtractionRestrictionFails()
        {
            var programDataDorc = Path.Join(_scratch, "programdata-dorc-cleanup-on-acl-fail");
            RestrictedDirectory.EnsureProgramDataRoot(programDataDorc);

            var artifactsRoot = Path.Join(programDataDorc, RestrictedDirectory.ArtifactsDirectoryName);
            var requestDrop = Path.Join(artifactsRoot, Guid.NewGuid().ToString("N"));
            var extractedDropDir = Path.Join(requestDrop, "drop");
            Directory.CreateDirectory(extractedDropDir);
            File.WriteAllText(Path.Join(extractedDropDir, "deploy.ps1"), "Write-Host 'hello'");

            // Plant a multi-link hard link inside the extracted drop so RestrictExtractedArtifactDrop
            // throws IOException after DownloadAndExtract has already populated localPath on disk.
            var externalFile = Path.Join(_scratch, "external-hardlink-target.ps1");
            File.WriteAllText(externalFile, "Write-Host 'external'");
            TerraformPlanLifetimeTests.CreateHardLink(
                Path.Join(extractedDropDir, "planted-hardlink.ps1"),
                externalFile);

            const string artifactUrl = "https://api.github.com/repos/sefe/dorc/actions/artifacts/778/zip";
            var downloader = Substitute.For<IGitHubArtifactDownloader>();
            downloader.IsGitHubArtifactUrl(artifactUrl).Returns(true);
            downloader.DownloadAndExtract(artifactUrl).Returns(requestDrop);
            downloader.When(d => d.Cleanup(requestDrop)).Do(_ =>
            {
                if (Directory.Exists(requestDrop))
                {
                    Directory.Delete(requestDrop, recursive: true);
                }
            });

            var credentialSource = Substitute.For<IDeploymentCredentialSource>();
            credentialSource.Resolve(DeploymentTier.NonProduction, "nonprod-runner")
                .Returns(_ => DeploymentCredential.FromPlainText("LOCAL SERVICE", "unused"));

            var configSettings = Substitute.For<IConfigurationSettings>();
            configSettings.GetConfigurationDomainNameIntra().Returns("NT AUTHORITY");

            var loggerFactory = Substitute.For<ILoggerFactory>();
            loggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());

            var requestsPds = Substitute.For<IRequestsPersistentSource>();
            requestsPds.SetRequestStartStatus(
                Arg.Any<DeploymentRequestApiModel>(),
                Arg.Any<DeploymentRequestStatus>(),
                Arg.Any<DateTimeOffset>()).Returns(true);
            var component = new ComponentApiModel { ComponentId = 1, ComponentName = "DeployComp", IsEnabled = true };
            requestsPds.GetSubmittedComponents(89).Returns(new List<ComponentApiModel> { component });
            requestsPds.GetDeploymentResultsForRequest(89).Returns(new List<DeploymentResultApiModel>
            {
                new() { Id = 1, ComponentId = 1, RequestId = 89, Status = DeploymentResultStatus.Pending.ToString() }
            });

            var envsPds = Substitute.For<IEnvironmentsPersistentSource>();
            envsPds.GetEnvironment("DevEnv").Returns(new EnvironmentApiModel
            {
                EnvironmentId = 10,
                EnvironmentName = "DevEnv",
                ExecutionIdentityReference = "nonprod-runner"
            });

            var configValuesPds = Substitute.For<IConfigValuesPersistentSource>();
            configValuesPds.GetConfigValue("ScriptRoot").Returns(@"C:\Scripts");
            configValuesPds.GetConfigValue("ScriptRoot", Arg.Any<string>()).Returns(@"C:\Scripts");
            configValuesPds.GetConfigValue("DeploymentLogDir", Arg.Any<string>()).Returns(@"C:\Logs");
            configValuesPds.GetAllConfigValues(true).Returns(Array.Empty<ConfigValueApiModel>());

            var propValuesPds = Substitute.For<IPropertyValuesPersistentSource>();
            propValuesPds.LoadAllPropertiesIntoCache().Returns(new Dictionary<string, PropertyValueDto>());

            var eventsPublisher = Substitute.For<IDeploymentEventsPublisher>();
            eventsPublisher.PublishRequestStatusChangedAsync(Arg.Any<DeploymentRequestEventData>()).Returns(Task.CompletedTask);
            eventsPublisher.PublishResultStatusChangedAsync(Arg.Any<DeploymentResultEventData>()).Returns(Task.CompletedTask);

            var componentProcessor = Substitute.For<IComponentProcessor>();
            var processor = new PendingRequestProcessor(
                loggerFactory,
                componentProcessor,
                Substitute.For<IVariableScopeOptionsResolver>(),
                requestsPds,
                propValuesPds,
                envsPds,
                Substitute.For<IManageProjectsPersistentSource>(),
                configValuesPds,
                Substitute.For<IPropertyEvaluator>(),
                eventsPublisher,
                Substitute.For<IDeploymentNotificationSink>(),
                downloader,
                Substitute.For<ISourceHostAllowList>(),
                new ScriptScopeConfigValues(new ConfigurationBuilder().Build()),
                credentialSource,
                configSettings);

            var dto = new RequestToProcessDto(
                new DeploymentRequestApiModel
                {
                    Id = 89,
                    EnvironmentName = "DevEnv",
                    IsProd = false,
                    BuildNumber = "1.2.4",
                    RequestDetails = "<DeploymentRequestDetail />"
                },
                new DeploymentRequestDetail
                {
                    EnvironmentName = "DevEnv",
                    Components = new List<string> { "DeployComp" },
                    BuildDetail = new BuildDetail { DropLocation = artifactUrl }
                });

            processor.Execute(dto, CancellationToken.None, loggerFactory);

            downloader.Received(1).DownloadAndExtract(artifactUrl);
            downloader.Received(1).Cleanup(requestDrop);
            Assert.IsFalse(
                Directory.Exists(requestDrop),
                "Extracted GitHub artifact directory must be deleted when post-extraction ACL restriction fails.");
            Assert.IsTrue(File.Exists(externalFile), "External hard-link target must remain intact.");
            componentProcessor.DidNotReceiveWithAnyArgs().DeployComponent(
                default!,
                default!,
                default,
                default,
                default,
                default,
                default!,
                default,
                default!,
                default!,
                default!,
                default);
            requestsPds.Received(1).SetRequestCompletionStatus(
                89,
                DeploymentRequestStatus.Errored,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string>());
        }
    }

    /// <summary>
    /// The bundle's file name is composed from HostInstanceId, which can be set from the
    /// environment, and it is not only written but DELETED. Nothing that reaches the writer
    /// today carries a separator - but the writer cannot check that its caller is trustworthy,
    /// only that the name it was given names one file in the directory it is meant to name it
    /// in.
    /// </summary>
    [TestClass]
    [SupportedOSPlatform("windows")]
    public class ScriptGroupBundleNameTests
    {
        private static ScriptGroupFileWriter NewWriter() =>
            new(Substitute.For<ILogger<ScriptGroupFileWriter>>());

        [TestMethod]
        [DataRow(@"..\..\Windows\System32\config\evil")]
        [DataRow("../../evil")]
        [DataRow(@"sub\DOrcMonitor-host-42")]
        [DataRow("sub/DOrcMonitor-host-42")]
        [DataRow(@"c:\Windows\Temp\DOrcMonitor-host-42")]
        [DataRow(@"\\other-host\share\DOrcMonitor-host-42")]
        [DataRow("..")]
        [DataRow("")]
        [DataRow("   ")]
        public void ABundleNameThatEscapesTheBundleDirectoryIsRefused(string pipeName)
        {
            Assert.ThrowsExactly<ArgumentException>(() => NewWriter().Start(
                pipeName,
                new ScriptGroup(),
                new ScriptGroupReaderIdentity("CORP", "svc-dorc"),
                CancellationToken.None));
        }

        /// <summary>
        /// Expire is called from a finally block on the deployment path. A name it refuses must
        /// leave the deployment's own outcome standing, not replace it with an argument
        /// failure raised while cleaning up.
        /// </summary>
        [TestMethod]
        public void ARefusedBundleNameDoesNotThrowOutOfExpiry()
        {
            NewWriter().Expire(@"..\..\evil");
        }
    }
}
