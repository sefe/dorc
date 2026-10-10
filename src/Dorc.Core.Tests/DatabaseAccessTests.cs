using Dorc.ApiModel;
using Dorc.Core.DatabaseAccess;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

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
        Assert.AreEqual("\\00\\2A\\FF", ActiveDirectorySearcher.EncodeLdapBytes([0, 42, 255]));
        Assert.ThrowsExactly<ArgumentException>(() => new DatabaseAccessDirectory(provider).Search("ab"));
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
