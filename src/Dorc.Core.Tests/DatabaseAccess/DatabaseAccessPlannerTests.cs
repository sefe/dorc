using Dorc.ApiModel;
using Dorc.Core.DatabaseAccess;
using NSubstitute;

namespace Dorc.Core.Tests.DatabaseAccess;

[TestClass]
public class DatabaseAccessPlannerTests
{
    [TestMethod]
    public void ProviderFor_MapsSupportedDatabaseTypesAndRejectsUnknown()
    {
        Assert.AreEqual("sql-server", DatabaseAccessPlanner.ProviderFor(DatabaseType.SqlServer));
        Assert.AreEqual("cosmos-db", DatabaseAccessPlanner.ProviderFor(DatabaseType.CosmosDb));
        Assert.AreEqual("clickhouse", DatabaseAccessPlanner.ProviderFor(DatabaseType.ClickHouse));
        Assert.AreEqual("postgresql", DatabaseAccessPlanner.ProviderFor(DatabaseType.PostgreSql));
        Assert.ThrowsExactly<NotSupportedException>(() => DatabaseAccessPlanner.ProviderFor((DatabaseType)999));
    }

    [TestMethod]
    public void Providers_ResolvesRegisteredNamesAndRejectsNullOrUnknown()
    {
        var sqlProvider = Substitute.For<IDatabaseAccessProvider>();
        sqlProvider.Name.Returns("sql-server");
        var providers = new DatabaseAccessProviders([sqlProvider]);

        CollectionAssert.AreEqual(new[] { "sql-server" }, providers.Names);
        Assert.AreSame(sqlProvider, providers.Resolve("sql-server"));
        Assert.ThrowsExactly<NotSupportedException>(() => providers.Resolve("postgresql"));
        Assert.ThrowsExactly<NotSupportedException>(() => providers.Resolve(null!));
    }

    [TestMethod]
    public void IdentifierAndProtectionRules_EnforceBoundsAndSystemNames()
    {
        DatabaseAccessPlanner.Identifier("valid_identifier_1");
        Assert.ThrowsExactly<ArgumentException>(() => DatabaseAccessPlanner.Identifier(null));
        Assert.ThrowsExactly<ArgumentException>(() => DatabaseAccessPlanner.Identifier("   "));
        Assert.ThrowsExactly<ArgumentException>(() => DatabaseAccessPlanner.Identifier(new string('a', 129)));
        Assert.ThrowsExactly<ArgumentException>(() => DatabaseAccessPlanner.Identifier(" padded"));
        Assert.ThrowsExactly<ArgumentException>(() => DatabaseAccessPlanner.Identifier("bad\0char"));

        Assert.IsTrue(DatabaseAccessPlanner.ProtectedPrincipal("dbo"));
        Assert.IsTrue(DatabaseAccessPlanner.ProtectedPrincipal("##MS_PolicyEventProcessingLogin##"));
        Assert.IsFalse(DatabaseAccessPlanner.ProtectedPrincipal("app_user"));

        Assert.IsTrue(DatabaseAccessPlanner.ProtectedRole("public"));
        Assert.IsTrue(DatabaseAccessPlanner.ProtectedRole("db_owner"));
        Assert.IsFalse(DatabaseAccessPlanner.ProtectedRole("db_datareader"));
        Assert.IsTrue(DatabaseAccessPlanner.IsDbOwner("DB_OWNER"));
        Assert.IsFalse(DatabaseAccessPlanner.IsDbOwner("db_datareader"));
    }

    [TestMethod]
    public void Validate_EnforcesProviderCollectionPrincipalRoleAndMembershipInvariants()
    {
        static void ExpectInvalid(DatabaseAccessState state) =>
            Assert.ThrowsExactly<ArgumentException>(() => DatabaseAccessPlanner.Validate(state));

        ExpectInvalid(new DatabaseAccessState { Provider = "" });
        ExpectInvalid(new DatabaseAccessState { Provider = new string('p', 41) });
        ExpectInvalid(new DatabaseAccessState { Principals = null! });
        ExpectInvalid(new DatabaseAccessState { Roles = null! });
        ExpectInvalid(new DatabaseAccessState { Memberships = null! });

        ExpectInvalid(new DatabaseAccessState
        {
            Principals = Enumerable.Range(0, 1001).Select(i => new DatabaseAccessPrincipal { Name = $"u{i}", LoginName = $"u{i}" }).ToList()
        });
        ExpectInvalid(new DatabaseAccessState
        {
            Roles = Enumerable.Range(0, 1001).Select(i => new DatabaseAccessRole { Name = $"r{i}", Managed = true }).ToList()
        });
        ExpectInvalid(new DatabaseAccessState
        {
            Memberships = Enumerable.Range(0, 10001).Select(i => new DatabaseAccessMembership { Principal = "u", Role = $"r{i}" }).ToList()
        });

        ExpectInvalid(new DatabaseAccessState { Principals = [null!] });
        ExpectInvalid(new DatabaseAccessState { Roles = [null!] });
        ExpectInvalid(new DatabaseAccessState { Memberships = [null!] });

        // Duplicate / protected / malformed principals
        ExpectInvalid(new DatabaseAccessState
        {
            Principals =
            [
                new() { Name = "dup", Kind = "Native", LoginName = "dup1" },
                new() { Name = "DUP", Kind = "Native", LoginName = "dup2" }
            ]
        });
        ExpectInvalid(new DatabaseAccessState { Principals = [new() { Name = "dbo", Kind = "Native", LoginName = "app" }] });
        ExpectInvalid(new DatabaseAccessState { Principals = [new() { Name = "app", Kind = "Native", LoginName = "app", DirectoryId = "S-1" }] });
        ExpectInvalid(new DatabaseAccessState { Principals = [new() { Name = "app", Kind = "Native", LoginName = "dbo" }] });
        ExpectInvalid(new DatabaseAccessState { Principals = [new() { Name = "ad", Kind = "DirectoryUser", DirectoryId = "" }] });
        ExpectInvalid(new DatabaseAccessState { Principals = [new() { Name = "ad", Kind = "DirectoryUser", DirectoryId = new string('s', 257) }] });
        ExpectInvalid(new DatabaseAccessState { Principals = [new() { Name = "ad", Kind = "DirectoryGroup", DirectoryId = "S-1", LoginName = "no" }] });
        ExpectInvalid(new DatabaseAccessState { Principals = [new() { Name = "x", Kind = "Certificate" }] });

        // Roles: duplicate, collides with principal, protected, managed db_*, reference-only absent
        ExpectInvalid(new DatabaseAccessState
        {
            Roles = [new() { Name = "app_role", Managed = true }, new() { Name = "APP_ROLE", Managed = true }]
        });
        ExpectInvalid(new DatabaseAccessState
        {
            Principals = [new() { Name = "clash", Kind = "Native", LoginName = "clash" }],
            Roles = [new() { Name = "clash", Managed = true }]
        });
        ExpectInvalid(new DatabaseAccessState { Roles = [new() { Name = "public", Managed = false, Present = true }] });
        ExpectInvalid(new DatabaseAccessState { Roles = [new() { Name = "db_owner", Managed = true, Present = true }] });
        ExpectInvalid(new DatabaseAccessState { Roles = [new() { Name = "db_owner", Managed = false, Present = false }] });
        ExpectInvalid(new DatabaseAccessState { Roles = [new() { Name = "db_custom", Managed = true, Present = true }] });
        ExpectInvalid(new DatabaseAccessState { Roles = [new() { Name = "db_datareader", Managed = false, Present = false }] });

        // Memberships: missing principal, missing role, duplicate, unapproved db_owner, present on absent principal/role
        ExpectInvalid(new DatabaseAccessState
        {
            Roles = [new() { Name = "db_datareader", Managed = false, Present = true }],
            Memberships = [new() { Principal = "missing", Role = "db_datareader" }]
        });
        ExpectInvalid(new DatabaseAccessState
        {
            Principals = [new() { Name = "app", Kind = "Native", LoginName = "app" }],
            Memberships = [new() { Principal = "app", Role = "missing" }]
        });
        ExpectInvalid(new DatabaseAccessState
        {
            Principals = [new() { Name = "app", Kind = "Native", LoginName = "app" }],
            Roles = [new() { Name = "db_datareader", Managed = false, Present = true }],
            Memberships =
            [
                new() { Principal = "app", Role = "db_datareader" },
                new() { Principal = "APP", Role = "DB_DATAREADER" }
            ]
        });
        // Valid reference-only db_owner membership on a new principal (approval is enforced by transition authorization)
        DatabaseAccessPlanner.Validate(new DatabaseAccessState
        {
            Principals = [new() { Name = "app", Kind = "Native", LoginName = "app", Present = true }],
            Roles = [new() { Name = "db_owner", Managed = false, Present = true }],
            Memberships = [new() { Principal = "app", Role = "db_owner", Present = true }]
        });
        ExpectInvalid(new DatabaseAccessState
        {
            Principals = [new() { Name = "app", Kind = "Native", LoginName = "app", Present = false }],
            Roles = [new() { Name = "db_datareader", Managed = false, Present = true }],
            Memberships = [new() { Principal = "app", Role = "db_datareader", Present = true }]
        });
        ExpectInvalid(new DatabaseAccessState
        {
            Principals = [new() { Name = "app", Kind = "Native", LoginName = "app", Present = true }],
            Roles = [new() { Name = "custom_role", Managed = true, Present = false }],
            Memberships = [new() { Principal = "app", Role = "custom_role", Present = true }]
        });

        // Valid state with approved db_owner, directory group, and revoked membership on absent managed role
        DatabaseAccessPlanner.Validate(new DatabaseAccessState
        {
            DatabaseId = 1,
            Provider = "sql-server",
            Principals =
            [
                new() { Name = "legacy_dbo", Kind = "Native", LoginName = "legacy_dbo", Present = true, LegacyUserId = 42 },
                new() { Name = "DOMAIN\\grp", Kind = "DirectoryGroup", DirectoryId = "S-1-5-21-1", Present = true }
            ],
            Roles =
            [
                new() { Name = "db_owner", Managed = false, Present = true },
                new() { Name = "old_role", Managed = true, Present = false }
            ],
            Memberships =
            [
                new() { Principal = "legacy_dbo", Role = "db_owner", Present = true },
                new() { Principal = "DOMAIN\\grp", Role = "old_role", Present = false }
            ]
        });
    }

    [TestMethod]
    public void Plan_ComputesOperationsAndReportsTargetDriftAndConstraintErrors()
    {
        Assert.ThrowsExactly<NotSupportedException>(() =>
            DatabaseAccessPlanner.Plan(new DatabaseAccessState { Provider = "postgresql" }, new DatabaseAccessObservation()));

        // Duplicate desired login mapping error + reference-only missing role + missing/protected/colliding/mismatched logins + unmanaged drop errors
        var errorState = new DatabaseAccessState
        {
            DatabaseId = 1,
            Revision = 2,
            Provider = "sql-server",
            Principals =
            [
                new() { Name = "u_dup1", Kind = "Native", LoginName = "shared_login", Present = true },
                new() { Name = "u_dup2", Kind = "Native", LoginName = "shared_login", Present = true },
                new() { Name = "u_nologin", Kind = "DirectoryUser", DirectoryId = "S-missing", Present = true },
                new() { Name = "u_protlogin", Kind = "Native", LoginName = "sa_disabled", Present = true },
                new() { Name = "u_takenlogin", Kind = "Native", LoginName = "taken_login", Present = true },
                new() { Name = "u_protuser", Kind = "Native", LoginName = "login_protuser", Present = true },
                new() { Name = "u_wrongkind", Kind = "DirectoryUser", DirectoryId = "S-wrongkind", Present = true },
                new() { Name = "u_drop_prot", Kind = "Native", LoginName = "l1", Present = false },
                new() { Name = "u_drop_unmanaged", Kind = "Native", LoginName = "l2", Present = false },
                new() { Name = "u_drop_gone", Kind = "Native", LoginName = "l3", Present = false }
            ],
            Roles =
            [
                new() { Name = "missing_ref_role", Managed = false, Present = true },
                new() { Name = "role_drop_unmanaged", Managed = true, Present = false },
                new() { Name = "role_drop_gone", Managed = true, Present = false }
            ],
            Memberships = []
        };

        var errorObs = new DatabaseAccessObservation
        {
            Logins =
            [
                new() { Name = "shared_login", LoginName = "shared_login", Kind = "Native" },
                new() { Name = "sa_disabled", LoginName = "sa_disabled", Kind = "Native", Protected = true },
                new() { Name = "taken_login", LoginName = "taken_login", Kind = "Native" },
                new() { Name = "login_protuser", LoginName = "login_protuser", Kind = "Native" },
                new() { Name = "AD\\wk", LoginName = "AD\\wk", Kind = "DirectoryUser", DirectoryId = "S-wrongkind" }
            ],
            Principals =
            [
                new() { Name = "other_db_user", Kind = "Native", LoginName = "taken_login" },
                new() { Name = "u_protuser", Kind = "Native", LoginName = "login_protuser", Protected = true },
                new() { Name = "u_wrongkind", Kind = "Native", LoginName = "old_native" },
                new() { Name = "u_drop_prot", Kind = "Native", LoginName = "l1", Protected = true },
                new() { Name = "u_drop_unmanaged", Kind = "Native", LoginName = "l2" }
            ],
            Roles =
            [
                new() { Name = "role_drop_unmanaged", Managed = true },
                new() { Name = "db_datareader", Managed = false }
            ],
            Memberships =
            [
                new() { Principal = "u_drop_unmanaged", Role = "db_datareader" },
                new() { Principal = "some_other_user", Role = "role_drop_unmanaged" }
            ]
        };

        var errorPlan = DatabaseAccessPlanner.Plan(errorState, errorObs);
        Assert.IsGreaterThanOrEqualTo(9, errorPlan.Errors.Count);

        // Clean convergence & remapping plan: CreateRole, CreateUser, MapUser (Native + Directory Sid), GrantRole, RevokeRole, DropUser, DropRole, and already-converged principals
        var cleanState = new DatabaseAccessState
        {
            DatabaseId = 1,
            Revision = 5,
            Provider = "sql-server",
            Principals =
            [
                new() { Name = "u_create", Kind = "Native", LoginName = "login_create", Present = true },
                new() { Name = "u_remap_login", Kind = "Native", LoginName = "login_new", Present = true },
                new() { Name = "u_remap_sid", Kind = "DirectoryUser", DirectoryId = "S-new", Present = true },
                new() { Name = "u_converged_native", Kind = "Native", LoginName = "login_conv", Present = true },
                new() { Name = "u_converged_ad", Kind = "DirectoryGroup", DirectoryId = "S-conv", Present = true },
                new() { Name = "u_drop_clean", Kind = "Native", LoginName = "login_drop", Present = false }
            ],
            Roles =
            [
                new() { Name = "app_readers", Managed = true, Present = true },
                new() { Name = "db_datareader", Managed = false, Present = true },
                new() { Name = "retired_role", Managed = true, Present = false }
            ],
            Memberships =
            [
                new() { Principal = "u_create", Role = "app_readers", Present = true },
                new() { Principal = "u_converged_native", Role = "db_datareader", Present = true },
                new() { Principal = "u_drop_clean", Role = "retired_role", Present = false }
            ]
        };

        var cleanObs = new DatabaseAccessObservation
        {
            Logins =
            [
                new() { Name = "login_create", LoginName = "login_create", Kind = "Native" },
                new() { Name = "login_new", LoginName = "login_new", Kind = "Native" },
                new() { Name = "AD\\renamed", LoginName = "AD\\renamed", Kind = "DirectoryUser", DirectoryId = "S-new" },
                new() { Name = "login_conv", LoginName = "login_conv", Kind = "Native" },
                new() { Name = "AD\\grp", LoginName = "AD\\grp", Kind = "DirectoryGroup", DirectoryId = "S-conv" }
            ],
            Principals =
            [
                new() { Name = "u_remap_login", Kind = "Native", LoginName = "login_old" },
                new() { Name = "u_remap_sid", Kind = "DirectoryUser", LoginName = "AD\\renamed", DirectoryId = "S-old" },
                new() { Name = "u_converged_native", Kind = "Native", LoginName = "login_conv" },
                new() { Name = "u_converged_ad", Kind = "DirectoryGroup", LoginName = "AD\\grp", DirectoryId = "S-conv" },
                new() { Name = "u_drop_clean", Kind = "Native", LoginName = "login_drop" }
            ],
            Roles =
            [
                new() { Name = "db_datareader", Managed = false },
                new() { Name = "retired_role", Managed = true }
            ],
            Memberships =
            [
                new() { Principal = "u_converged_native", Role = "db_datareader" },
                new() { Principal = "u_drop_clean", Role = "retired_role" }
            ]
        };

        var cleanPlan = DatabaseAccessPlanner.Plan(cleanState, cleanObs);
        Assert.HasCount(0, cleanPlan.Errors);
        CollectionAssert.AreEqual(
            new[] { "CreateRole", "CreateUser", "MapUser", "MapUser", "GrantRole", "RevokeRole", "DropUser", "DropRole" },
            cleanPlan.Operations.Select(o => o.Action).ToArray());
        Assert.IsFalse(string.IsNullOrWhiteSpace(cleanPlan.Token));

        var confirm = new DatabaseAccessConfirmation { Token = cleanPlan.Token };
        Assert.AreEqual(cleanPlan.Token, confirm.Token);
    }
}
