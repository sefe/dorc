using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Contexts;

// No retry strategy: a lost lock/connection must not replay external database operations.
public sealed class DatabaseAccessContext(DbContextOptions<DatabaseAccessContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        var configuration = model.Entity<DatabaseAccessConfiguration>();
        configuration.ToTable("DatabaseAccessConfiguration", "deploy");
        configuration.HasKey(x => x.DatabaseId);
        configuration.Property(x => x.DatabaseId).ValueGeneratedNever();
        configuration.Property(x => x.Provider).HasMaxLength(40);
        configuration.Property(x => x.Revision).IsConcurrencyToken();

        var principalCatalog = model.Entity<DatabaseAccessPrincipalCatalog>();
        principalCatalog.ToTable("DatabaseAccessPrincipalCatalog", "deploy");
        principalCatalog.HasKey(x => x.Id);
        principalCatalog.Property(x => x.Name).HasMaxLength(128);
        principalCatalog.Property(x => x.Kind).HasMaxLength(32);
        principalCatalog.Property(x => x.LoginName).HasMaxLength(128);
        principalCatalog.Property(x => x.DirectoryId).HasMaxLength(256);
        principalCatalog.HasIndex(x => new { x.Kind, x.Name, x.LoginName, x.DirectoryId }).IsUnique();

        var roleCatalog = model.Entity<DatabaseAccessRoleCatalog>();
        roleCatalog.ToTable("DatabaseAccessRoleCatalog", "deploy");
        roleCatalog.HasKey(x => x.Id);
        roleCatalog.Property(x => x.Name).HasMaxLength(128);
        roleCatalog.HasIndex(x => x.Name).IsUnique();

        var principalBinding = model.Entity<DatabaseAccessPrincipalBinding>();
        principalBinding.ToTable("DatabaseAccessPrincipalBinding", "deploy");
        principalBinding.HasKey(x => new { x.DatabaseId, x.PrincipalId });
        principalBinding.HasOne<DatabaseAccessConfiguration>().WithMany().HasForeignKey(x => x.DatabaseId)
            .OnDelete(DeleteBehavior.Restrict);
        principalBinding.HasOne<DatabaseAccessPrincipalCatalog>().WithMany().HasForeignKey(x => x.PrincipalId)
            .OnDelete(DeleteBehavior.Restrict);

        var roleBinding = model.Entity<DatabaseAccessRoleBinding>();
        roleBinding.ToTable("DatabaseAccessRoleBinding", "deploy");
        roleBinding.HasKey(x => new { x.DatabaseId, x.RoleId });
        roleBinding.HasOne<DatabaseAccessConfiguration>().WithMany().HasForeignKey(x => x.DatabaseId)
            .OnDelete(DeleteBehavior.Restrict);
        roleBinding.HasOne<DatabaseAccessRoleCatalog>().WithMany().HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        var membership = model.Entity<DatabaseAccessRoleMembership>();
        membership.ToTable("DatabaseAccessRoleMembership", "deploy");
        membership.HasKey(x => new { x.DatabaseId, x.PrincipalId, x.RoleId });
        membership.HasOne<DatabaseAccessPrincipalBinding>().WithMany()
            .HasForeignKey(x => new { x.DatabaseId, x.PrincipalId }).OnDelete(DeleteBehavior.Restrict);
        membership.HasOne<DatabaseAccessRoleBinding>().WithMany()
            .HasForeignKey(x => new { x.DatabaseId, x.RoleId }).OnDelete(DeleteBehavior.Restrict);

        var audit = model.Entity<DatabaseAccessAudit>();
        audit.ToTable("DatabaseAccessAudit", "deploy");
        audit.HasKey(x => x.Id);
        audit.Property(x => x.Actor).HasMaxLength(512);
        audit.Property(x => x.Action).HasMaxLength(32);
        audit.Property(x => x.Status).HasMaxLength(32);
        audit.HasIndex(x => new { x.DatabaseId, x.CreatedUtc });
    }
}
