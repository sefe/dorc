using Dorc.ApiModel;
using Dorc.Core.DatabaseAccess;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using System.Security.Principal;

namespace Dorc.Core.Tests;

[TestClass]
public class DatabaseAccessTests
{
    private static DatabaseAccessState Desired() => new()
    {
        DatabaseId = 1, Revision = 3,
        Principals = [new() { Name = "app", LoginName = "app_login", Kind = "Native" }],
        Roles = [new() { Name = "reader", Managed = true }],
        Memberships = [new() { Principal = "app", Role = "reader" }]
    };

    private static DatabaseAccessObservation Observed() => new()
    {
        Logins = [new() { Name = "app_login", LoginName = "app_login", Kind = "Native" }]
    };

    [TestMethod]
    public void PlansOnlyManagedStateInDependencyOrder()
    {
        var observed = Observed();
        observed.Principals.Add(new() { Name = "unmanaged", Kind = "Native" });
        var plan = DatabaseAccessPlanner.Plan(Desired(), observed);
        Assert.HasCount(0, plan.Errors);
        CollectionAssert.AreEqual(new[] { "CreateRole", "CreateUser", "GrantRole" }, plan.Operations.Select(o => o.Action).ToArray());
        Assert.IsFalse(plan.Operations.Any(o => o.Principal == "unmanaged"));
    }

    [TestMethod]
    public void MatchingStateIsANoop()
    {
        var observed = Observed();
        observed.Principals.Add(new() { Name = "app", Kind = "Native", LoginName = "app_login" });
        observed.Roles.Add(new() { Name = "reader" });
        observed.Memberships.Add(new() { Principal = "app", Role = "reader" });
        var plan = DatabaseAccessPlanner.Plan(Desired(), observed);
        Assert.HasCount(0, plan.Errors);
        Assert.HasCount(0, plan.Operations);
    }

    [TestMethod]
    public void MissingLoginIsExplicitlyUnsupported()
    {
        var plan = DatabaseAccessPlanner.Plan(Desired(), new());
        Assert.IsTrue(plan.Errors.Any(e => e.Contains("Server-login provisioning is not supported")));
    }

    [TestMethod]
    public void DuplicateLoginMappingsAreRejectedBeforeTargetMutation()
    {
        var desired = Desired();
        desired.Principals.Add(new() { Name = "second_alias", LoginName = "app_login" });
        Assert.IsTrue(DatabaseAccessPlanner.Plan(desired, Observed()).Errors.Any(e => e.Contains("Duplicate desired login")));
    }

    [TestMethod]
    public void ExistingUnmanagedLoginMappingIsReportedRatherThanReassigned()
    {
        var observed = Observed();
        observed.Principals.Add(new() { Name = "other_alias", Kind = "Native", LoginName = "app_login" });
        Assert.IsTrue(DatabaseAccessPlanner.Plan(Desired(), observed).Errors.Any(e => e.Contains("already mapped")));
    }

    [TestMethod]
    public void TokensChangeWithDesiredAndObservedState()
    {
        var desired = Desired();
        var observed = Observed();
        var first = DatabaseAccessPlanner.Plan(desired, observed).Token;
        desired.Revision++;
        Assert.AreNotEqual(first, DatabaseAccessPlanner.Plan(desired, observed).Token);
        desired.Revision--;
        observed.Roles.Add(new() { Name = "reader" });
        Assert.AreNotEqual(first, DatabaseAccessPlanner.Plan(desired, observed).Token);
    }

    [TestMethod]
    public void RejectsUnsupportedProvider()
    {
        var state = Desired();
        state.Provider = "postgresql";
        Assert.ThrowsExactly<NotSupportedException>(() => new DatabaseAccessProviders([new SqlServerDatabaseAccessProvider()]).Resolve(state.Provider));
        Assert.ThrowsExactly<NotSupportedException>(() => DatabaseAccessPlanner.Plan(state, Observed()));
    }

    [TestMethod]
    public void RejectsDuplicateAndCrossDatabaseStyleReferences()
    {
        var state = Desired();
        state.Principals.Add(new() { Name = "APP", LoginName = "other", Kind = "Native" });
        Assert.ThrowsExactly<ArgumentException>(() => DatabaseAccessPlanner.Validate(state));
        state = Desired();
        state.Memberships[0].Principal = "outside";
        Assert.ThrowsExactly<ArgumentException>(() => DatabaseAccessPlanner.Validate(state));
    }

    [TestMethod]
    public void RejectsProtectedPrincipalsAndRoles()
    {
        var state = Desired();
        state.Principals[0].Name = "dbo";
        Assert.ThrowsExactly<ArgumentException>(() => DatabaseAccessPlanner.Validate(state));
        state = Desired();
        state.Roles[0].Name = "db_owner";
        Assert.ThrowsExactly<ArgumentException>(() => DatabaseAccessPlanner.Validate(state));
    }

    [TestMethod]
    public void SqlIdentifiersCannotEscapeTheirQuotedScope()
    {
        var sql = SqlServerDatabaseAccessProvider.CommandText(new() { Action = "CreateRole", Role = "role]; DROP TABLE x;--" });
        Assert.AreEqual("CREATE ROLE [role]]; DROP TABLE x;--] AUTHORIZATION [dbo];", sql);
        Assert.ThrowsExactly<NotSupportedException>(() => SqlServerDatabaseAccessProvider.CommandText(new() { Action = "CreateLogin" }));
    }

    [TestMethod]
    public void RevokesBeforeDroppingAndBlocksUnmanagedMemberships()
    {
        var desired = Desired();
        desired.Principals[0].Present = false;
        desired.Roles[0].Present = false;
        desired.Memberships[0].Present = false;
        var observed = Observed();
        observed.Principals.Add(new() { Name = "app", Kind = "Native" });
        observed.Roles.Add(new() { Name = "reader" });
        observed.Memberships.Add(new() { Principal = "app", Role = "reader" });
        var plan = DatabaseAccessPlanner.Plan(desired, observed);
        CollectionAssert.AreEqual(new[] { "RevokeRole", "DropUser", "DropRole" }, plan.Operations.Select(o => o.Action).ToArray());
        observed.Memberships.Add(new() { Principal = "unmanaged", Role = "reader" });
        Assert.IsTrue(DatabaseAccessPlanner.Plan(desired, observed).Errors.Any());
    }

    [TestMethod]
    public void TransitionCannotForgetOwnershipOrForgeProvenance()
    {
        var current = Desired();
        var desired = Desired();
        desired.Principals.Clear();
        Assert.ThrowsExactly<ArgumentException>(() => DatabaseAccessService.ValidateTransition(current, desired));
        desired = Desired();
        desired.Principals.Add(new() { Name = "forged", LoginName = "login", LegacyUserId = 10 });
        Assert.ThrowsExactly<ArgumentException>(() => DatabaseAccessService.ValidateTransition(current, desired));
        desired = Desired();
        desired.Revision--;
        Assert.ThrowsExactly<DatabaseAccessConflictException>(() => DatabaseAccessService.ValidateTransition(current, desired));
    }

    [TestMethod]
    public void ImportConvertsEndurAndRequiresExplicitRoleMappings()
    {
        var source = new[] { new LegacyDatabaseAccessAssignment(42, "app ", "Endur", "Read") };
        var request = new DatabaseAccessImportRequest();
        var blocked = DatabaseAccessImport.Build(new(), source, request);
        Assert.IsTrue(blocked.Errors.Any());
        request.RoleMappings.Add("Read", "db_datareader");
        var preview = DatabaseAccessImport.Build(new(), source, request);
        Assert.HasCount(0, preview.Errors);
        Assert.AreEqual("Native", preview.Proposed.Principals.Single().Kind);
        Assert.AreEqual("app", preview.Proposed.Principals.Single().LoginName);
        Assert.AreEqual(42, preview.Proposed.Principals.Single().LegacyUserId);
        var rerun = DatabaseAccessImport.Build(preview.Proposed, source, request);
        Assert.HasCount(1, rerun.Proposed.Principals);
        Assert.HasCount(1, rerun.Proposed.Memberships);
    }

    [TestMethod]
    public void ImportReportsConflictingUsersAndMissingDirectoryReferences()
    {
        var request = new DatabaseAccessImportRequest { RoleMappings = new() { ["Read"] = "db_datareader" } };
        var duplicate = DatabaseAccessImport.Build(new(), [
            new(1, "app", "Endur", "Read"), new(2, "app", "Sql", "Read")], request);
        Assert.IsTrue(duplicate.Errors.Any(e => e.Contains("conflicts")));
        var windows = DatabaseAccessImport.Build(new(), [new(3, "AD\\person", "Windows", "Read")], request);
        Assert.IsTrue(windows.Errors.Any(e => e.Contains("directory reference")));
    }

    [TestMethod]
    public void ImportExplicitlyExcludesAllAssignmentsForAnUnresolvedWindowsAccount()
    {
        LegacyDatabaseAccessAssignment[] source = [
            new(1, "app", "Endur", "Read"),
            new(2, "AD\\missing", "Windows", "Read"),
            new(2, "AD\\missing", "Windows", "OldRole")];
        var request = new DatabaseAccessImportRequest { RoleMappings = new() { ["Read"] = "db_datareader" } };
        var blocked = DatabaseAccessImport.Build(new(), source, request);
        Assert.IsTrue(blocked.Errors.Any(e => e.Contains("directory reference")));
        request.ExcludedLegacyUserIds.Add(2);
        var preview = DatabaseAccessImport.Build(new(), source, request);

        Assert.HasCount(0, preview.Errors);
        Assert.AreEqual(3, preview.SourceAssignments);
        Assert.AreEqual("app", preview.Proposed.Principals.Single().Name);
        Assert.HasCount(1, preview.Proposed.Memberships);
        var excluded = preview.ExcludedAccounts.Single();
        Assert.AreEqual(2, excluded.LegacyUserId);
        Assert.AreEqual("AD\\missing", excluded.LoginName);
        Assert.AreEqual(2, excluded.AssignmentCount);
        CollectionAssert.AreEquivalent(new[] { "Read", "OldRole" }, excluded.Permissions);
        Assert.AreNotEqual(blocked.Token, preview.Token);
        var changedSource = source.Append(new LegacyDatabaseAccessAssignment(2, "AD\\missing", "Windows", "AnotherRole")).ToArray();
        Assert.AreNotEqual(preview.Token, DatabaseAccessImport.Build(new(), changedSource, request).Token);
        request.ExcludedLegacyUserIds.Clear();
        Assert.AreEqual(blocked.Token, DatabaseAccessImport.Build(new(), source, request).Token);
    }

    [TestMethod]
    public void ImportRejectsOutOfScopeConflictingAndNonWindowsExclusions()
    {
        var request = new DatabaseAccessImportRequest
        {
            ExcludedLegacyUserIds = [0, 999, 1, 2, 3, 4, 4],
            DirectoryMappings = new() { [2] = "resolved" },
            RoleMappings = new() { ["Read"] = "db_datareader" }
        };
        var preview = DatabaseAccessImport.Build(new(), [
            new(1, "app", "Endur", "Read"),
            new(2, "AD\\mapped", "Windows", "Read"),
            new(3, "AD\\one", "Windows", "Read"),
            new(3, "AD\\other", "Windows", "Read"),
            new(4, "AD\\missing", "Windows", "Read")], request);
        Assert.IsTrue(preview.Errors.Any(e => e.Contains("Legacy user 0") && e.Contains("no assignments")));
        Assert.IsTrue(preview.Errors.Any(e => e.Contains("Legacy user 999") && e.Contains("no assignments")));
        Assert.IsTrue(preview.Errors.Any(e => e.Contains("Legacy user 1") && e.Contains("Windows")));
        Assert.IsTrue(preview.Errors.Any(e => e.Contains("Legacy user 2") && e.Contains("both mapped")));
        Assert.IsTrue(preview.Errors.Any(e => e.Contains("Legacy user 3") && e.Contains("consistent")));
        Assert.IsTrue(preview.Errors.Any(e => e.Contains("Legacy user 4") && e.Contains("more than once")));
        Assert.AreEqual(4, preview.ExcludedAccounts.Single().LegacyUserId);
        Assert.ThrowsExactly<ArgumentException>(() =>
            DatabaseAccessImport.Build(new(), [], new() { ExcludedLegacyUserIds = null! }));
    }

    [TestMethod]
    public void ImportExclusionsCannotForgetAlreadyManagedPrincipals()
    {
        LegacyDatabaseAccessAssignment[] source = [new(2, "AD\\missing", "Windows", "Read")];
        var request = new DatabaseAccessImportRequest
        {
            RoleMappings = new() { ["Read"] = "db_datareader" },
            DirectoryMappings = new() { [2] = "stable" }
        };
        var current = DatabaseAccessImport.Build(new(), source, request).Proposed;
        request.DirectoryMappings.Clear();
        request.ExcludedLegacyUserIds.Add(2);
        var preview = DatabaseAccessImport.Build(current, source, request);
        Assert.HasCount(0, preview.ExcludedAccounts);
        Assert.IsTrue(preview.Errors.Any(e => e.Contains("already in desired state")));
        Assert.AreEqual("stable", preview.Proposed.Principals.Single().DirectoryId);
        Assert.IsTrue(preview.Proposed.Memberships.Single().Present);
        current.Principals[0].LegacyUserId = null;
        Assert.IsTrue(DatabaseAccessImport.Build(current, source, request).Errors.Any(e => e.Contains("already in desired state")));
    }

    [TestMethod]
    public void ImportExclusionsDoNotBypassErrorsForIncludedAccounts()
    {
        var request = new DatabaseAccessImportRequest
        {
            ExcludedLegacyUserIds = [2],
            RoleMappings = new() { ["Owner"] = "db_owner", ["Read"] = "db_datareader" }
        };
        var preview = DatabaseAccessImport.Build(new(), [
            new(1, "app", "Endur", "Owner"),
            new(2, "AD\\excluded", "Windows", "Read"),
            new(3, "AD\\unresolved", "Windows", "Read")], request);
        Assert.AreEqual(2, preview.ExcludedAccounts.Single().LegacyUserId);
        Assert.IsTrue(preview.Errors.Any(e => e.Contains("explicit db_owner membership approval")));
        Assert.IsTrue(preview.Errors.Any(e => e.Contains("Windows user 3")));
    }

    [TestMethod]
    public void DbOwnerImportRequiresExplicitApprovalAndBindsItToThePreview()
    {
        LegacyDatabaseAccessAssignment[] source = [new(42, "app", "Endur", "db_owner")];
        var request = new DatabaseAccessImportRequest { RoleMappings = new() { ["db_owner"] = "db_owner" } };
        var blocked = DatabaseAccessImport.Build(new(), source, request);
        Assert.IsTrue(blocked.Errors.Any(e => e.Contains("explicit db_owner membership approval")));
        Assert.HasCount(0, blocked.Proposed.Memberships);
        request.ApprovedDbOwnerLegacyUserIds.Add(42);
        var approved = DatabaseAccessImport.Build(new(), source, request);
        Assert.HasCount(0, approved.Errors);
        Assert.AreEqual(42, approved.ApprovedDbOwnerLegacyUserIds.Single());
        Assert.IsFalse(approved.Proposed.Roles.Single().Managed);
        Assert.AreEqual("db_owner", approved.Proposed.Memberships.Single().Role);
        Assert.AreNotEqual(blocked.Token, approved.Token);
        Assert.AreNotEqual(approved.Token, DatabaseAccessImport.Build(new(),
            [new(42, "other_login", "Endur", "db_owner")], request).Token);
        request.ApprovedDbOwnerLegacyUserIds.Clear();
        Assert.HasCount(0, DatabaseAccessImport.Build(approved.Proposed, source, request).Errors,
            "Persisted approved memberships do not need new approval on an unchanged reimport.");
        approved.Proposed.Memberships[0].Present = false;
        Assert.IsTrue(DatabaseAccessImport.Build(approved.Proposed, source, request).Errors.Any());
        request.ApprovedDbOwnerLegacyUserIds.Add(42);
        var reapproved = DatabaseAccessImport.Build(approved.Proposed, source, request);
        Assert.HasCount(0, reapproved.Errors);
        Assert.IsTrue(reapproved.Proposed.Memberships.Single().Present);
    }

    [TestMethod]
    public void DbOwnerApprovalsCannotApplyOutsideTheSelectedLegacyMappings()
    {
        LegacyDatabaseAccessAssignment[] source = [new(42, "app", "Endur", "db_owner")];
        var request = new DatabaseAccessImportRequest
        {
            RoleMappings = new() { ["db_owner"] = "db_owner" }, ApprovedDbOwnerLegacyUserIds = [999, 42, 42]
        };
        var result = DatabaseAccessImport.Build(new(), source, request);
        Assert.IsTrue(result.Errors.Any(e => e.Contains("999") && e.Contains("no db_owner mapping")));
        Assert.IsTrue(result.Errors.Any(e => e.Contains("more than once")));
        request.ApprovedDbOwnerLegacyUserIds = [42];
        request.ExcludedLegacyUserIds = [42];
        Assert.IsTrue(DatabaseAccessImport.Build(new(), source, request).Errors.Any(e => e.Contains("both excluded and approved")));
        request.ExcludedLegacyUserIds.Clear();
        request.RoleMappings["db_owner"] = "db_datareader";
        Assert.IsTrue(DatabaseAccessImport.Build(new(), source, request).Errors.Any(e => e.Contains("no db_owner mapping")));
        Assert.ThrowsExactly<ArgumentException>(() =>
            DatabaseAccessImport.Build(new(), [], new() { ApprovedDbOwnerLegacyUserIds = null! }));
    }

    [TestMethod]
    public void OrdinarySavesCannotGrantDbOwnerOrTransferItsApprovedIdentity()
    {
        var request = new DatabaseAccessImportRequest
        {
            RoleMappings = new() { ["Owner"] = "db_owner" }, ApprovedDbOwnerLegacyUserIds = [42]
        };
        DatabaseAccessState State() => DatabaseAccessImport.Build(new(),
            [new(42, "app", "Endur", "Owner")], request).Proposed;
        var current = State();
        DatabaseAccessService.ValidateTransition(current, State());
        var changed = State();
        changed.Principals[0].LoginName = "other_login";
        Assert.ThrowsExactly<ArgumentException>(() => DatabaseAccessService.ValidateTransition(current, changed));
        changed = State();
        changed.Memberships[0].Present = false;
        DatabaseAccessService.ValidateTransition(current, changed);
        Assert.ThrowsExactly<ArgumentException>(() => DatabaseAccessService.ValidateTransition(changed, current));
        changed.Memberships.Clear();
        Assert.ThrowsExactly<ArgumentException>(() => DatabaseAccessService.ValidateTransition(changed, current));
    }

    [TestMethod]
    public void ApprovedDbOwnerMembershipsReconcileWithoutManagingProtectedRoleDefinitions()
    {
        var state = DatabaseAccessImport.Build(new(),
            [new(42, "app", "Endur", "Owner")], new()
            {
                RoleMappings = new() { ["Owner"] = "db_owner" }, ApprovedDbOwnerLegacyUserIds = [42]
            }).Proposed;
        var observed = new DatabaseAccessObservation
        {
            Logins = [new() { Name = "app", LoginName = "app", Kind = "Native" }],
            Principals = [new() { Name = "app", LoginName = "app", Kind = "Native" }],
            Roles = [new() { Name = "db_owner" }]
        };
        var plan = DatabaseAccessPlanner.Plan(state, observed);
        Assert.HasCount(0, plan.Errors);
        Assert.AreEqual("GrantRole", plan.Operations.Single().Action);
        observed.Memberships.Add(new() { Principal = "app", Role = "db_owner" });
        Assert.HasCount(0, DatabaseAccessPlanner.Plan(state, observed).Operations);
        state.Memberships[0].Present = false;
        Assert.AreEqual("RevokeRole", DatabaseAccessPlanner.Plan(state, observed).Operations.Single().Action);
        state.Roles[0].Managed = true;
        Assert.ThrowsExactly<ArgumentException>(() => DatabaseAccessPlanner.Validate(state));
        state.Roles[0].Managed = false;
        state.Roles[0].Present = false;
        Assert.ThrowsExactly<ArgumentException>(() => DatabaseAccessPlanner.Validate(state));
        state.Roles[0].Present = true;
        state.Principals[0].LegacyUserId = null;
        Assert.ThrowsExactly<ArgumentException>(() => DatabaseAccessPlanner.Validate(state));
    }

    [TestMethod]
    [DataRow("db_securityadmin")]
    [DataRow("db_accessadmin")]
    [DataRow("public")]
    public void DbOwnerApprovalDoesNotPermitOtherProtectedRoles(string role)
    {
        var result = DatabaseAccessImport.Build(new(),
            [new(42, "app", "Endur", "Owner"), new(42, "app", "Endur", "Other")], new()
            {
                RoleMappings = new() { ["Owner"] = "db_owner", ["Other"] = role },
                ApprovedDbOwnerLegacyUserIds = [42]
            });
        Assert.IsTrue(result.Errors.Any(e => e.Contains("protected role")));
    }

    [TestMethod]
    public void ImportDetectsLegacyRemovalAndLocalRevocation()
    {
        var request = new DatabaseAccessImportRequest { RoleMappings = new() { ["Read"] = "db_datareader" } };
        LegacyDatabaseAccessAssignment[] source = [new(1, "app", "Endur", "Read")];
        var first = DatabaseAccessImport.Build(new(), source, request);
        first.Proposed.Memberships[0].Present = false;
        Assert.IsTrue(DatabaseAccessImport.Build(first.Proposed, source, request).Errors.Any(e => e.Contains("diverged")));
        Assert.IsTrue(DatabaseAccessImport.Build(first.Proposed, [], request).Errors.Any(e => e.Contains("no longer exists")));
    }

    [TestMethod]
    public void ImportDetectsRemovedRoleEvenWhenUserStillExists()
    {
        var request = new DatabaseAccessImportRequest { RoleMappings = new() { ["Read"] = "db_datareader", ["Write"] = "db_datawriter" } };
        var original = DatabaseAccessImport.Build(new(), [new(1, "app", "Endur", "Read"), new(1, "app", "Endur", "Write")], request);
        var changed = DatabaseAccessImport.Build(original.Proposed, [new(1, "app", "Endur", "Read")], request);
        Assert.IsTrue(changed.Errors.Any(e => e.Contains("db_datawriter")));
    }

    [TestMethod]
    public void ImportPreservesMembershipsWithoutLegacyProvenance()
    {
        var current = Desired();
        var result = DatabaseAccessImport.Build(current, [], new());
        Assert.HasCount(0, result.Errors);
        Assert.HasCount(1, result.Proposed.Memberships);
        Assert.IsNull(result.Proposed.Principals.Single().LegacyUserId);
    }

    [TestMethod]
    public void DirectoryResolvesFreshDataAndDoesNotSwallowOutages()
    {
        var provider = Substitute.For<IActiveDirectorySearcher>();
        provider.GetUserDataById("stable").Returns(
            new UserElementApiModel { Pid = "stable", DisplayName = "Before" },
            new UserElementApiModel { Pid = "stable", DisplayName = "After" });
        var directory = new DatabaseAccessDirectory(provider);
        Assert.AreEqual("Before", directory.Resolve("stable").DisplayName);
        Assert.AreEqual("After", directory.Resolve("stable").DisplayName);
        provider.GetUserDataById("missing").Returns(_ => throw new ArgumentException("Not found"));
        Assert.ThrowsExactly<ArgumentException>(() => directory.Resolve("missing"));
        provider.GetUserDataById("outage").Returns(_ => throw new InvalidOperationException("Directory unavailable"));
        Assert.ThrowsExactly<InvalidOperationException>(() => directory.Resolve("outage"));
        provider.GetUserDataById("ambiguous").Returns(new UserElementApiModel { Pid = "other" });
        Assert.ThrowsExactly<ArgumentException>(() => directory.Resolve("ambiguous"));
    }

    [TestMethod]
    public void DirectoryGroupsUseStableReferencesAndBinaryLdapEscaping()
    {
        var provider = Substitute.For<IActiveDirectorySearcher>();
        provider.GetUserDataById("S-1-5-21").Returns(new UserElementApiModel { Sid = "S-1-5-21", DisplayName = "Group", IsGroup = true });
        Assert.IsTrue(new DatabaseAccessDirectory(provider).Resolve("S-1-5-21").IsGroup);
        Assert.AreEqual("\\00\\28\\29\\2A\\5C\\FF", ActiveDirectorySearcher.EncodeLdapBytes([0, 40, 41, 42, 92, 255]));
        Assert.ThrowsExactly<ArgumentException>(() => new DatabaseAccessDirectory(provider).Search("ab"));
    }

    [TestMethod]
    [DataRow("S-1-5-18", false)]
    [DataRow("S-1-5-11", true)]
    public void ExactDirectoryLookupVerifiesTheTranslatedSidWithoutFuzzySearch(string sid, bool isGroup)
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows account translation requires Windows.");
        var accountName = new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value;
        var provider = Substitute.For<IActiveDirectorySearcher>();
        provider.GetUserDataById(sid).Returns(new UserElementApiModel { Sid = sid, DisplayName = "Verified", IsGroup = isGroup });
        var result = new DatabaseAccessDirectory(provider).Lookup(accountName);
        Assert.AreEqual(sid, result.Id);
        Assert.AreEqual(isGroup, result.IsGroup);
        provider.Received(1).GetUserDataById(sid);
        provider.DidNotReceiveWithAnyArgs().Search(default!);
        provider.DidNotReceiveWithAnyArgs().GetUserData(default!);
    }

    [TestMethod]
    [DataRow("readers")]
    [DataRow("DOMAIN\\")]
    [DataRow("\\readers")]
    [DataRow("DOMAIN\\other\\readers")]
    [DataRow(" DOMAIN\\readers")]
    [DataRow("DOMAIN\\readers\n")]
    public void AutomaticDirectoryLookupRejectsUnqualifiedOrMalformedNames(string name)
    {
        var provider = Substitute.For<IActiveDirectorySearcher>();
        Assert.ThrowsExactly<ArgumentException>(() => new DatabaseAccessDirectory(provider).Lookup(name));
        provider.DidNotReceiveWithAnyArgs().GetUserDataById(default!);
    }

    [TestMethod]
    public void ExactDirectoryLookupCannotUseTranslationAloneWhenLiveDirectoryVerificationFails()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows account translation requires Windows.");
        var accountName = new SecurityIdentifier("S-1-5-18").Translate(typeof(NTAccount)).Value;
        var provider = Substitute.For<IActiveDirectorySearcher>();
        provider.GetUserDataById("S-1-5-18").Returns(_ => throw new ArgumentException("Directory identity is unavailable."));
        Assert.ThrowsExactly<ArgumentException>(() => new DatabaseAccessDirectory(provider).Lookup(accountName));
        provider.DidNotReceiveWithAnyArgs().Search(default!);
    }

    [TestMethod]
    public void FoundationUsesCompositeMembershipForeignKeysAndConcurrency()
    {
        using var context = new DatabaseAccessContext(new DbContextOptionsBuilder<DatabaseAccessContext>()
            .UseSqlServer("Server=(localdb)\\DOrcAccessTests;Database=Unused;Integrated Security=true").Options);
        var membership = context.Model.FindEntityType(typeof(DatabaseAccessMembershipRecord))!;
        Assert.HasCount(3, membership.FindPrimaryKey()!.Properties);
        Assert.IsTrue(membership.GetForeignKeys().All(fk => fk.Properties.Count == 2 && fk.DeleteBehavior == DeleteBehavior.Restrict));
        Assert.IsTrue(context.Model.FindEntityType(typeof(DatabaseAccessConfiguration))!.FindProperty("Revision")!.IsConcurrencyToken);
        Assert.IsNull(context.Model.FindEntityType(typeof(User)));
    }
}
