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

        var sharedPrincipal = model.Entity<DatabaseAccessSharedPrincipal>();
        sharedPrincipal.ToTable("DatabaseAccessSharedPrincipal", "deploy");
        sharedPrincipal.HasKey(x => x.Id);
        sharedPrincipal.Property(x => x.Name).HasMaxLength(128);
        sharedPrincipal.Property(x => x.Kind).HasMaxLength(32);
        sharedPrincipal.Property(x => x.LoginName).HasMaxLength(128);
        sharedPrincipal.Property(x => x.DirectoryId).HasMaxLength(256);
        sharedPrincipal.HasIndex(x => new { x.Kind, x.Name, x.LoginName, x.DirectoryId }).IsUnique();

        var sharedRole = model.Entity<DatabaseAccessSharedRole>();
        sharedRole.ToTable("DatabaseAccessSharedRole", "deploy");
        sharedRole.HasKey(x => x.Id);
        sharedRole.Property(x => x.Name).HasMaxLength(128);
        sharedRole.HasIndex(x => x.Name).IsUnique();

        var principal = model.Entity<DatabaseAccessDatabasePrincipal>();
        principal.ToTable("DatabaseAccessDatabasePrincipal", "deploy");
        principal.HasKey(x => new { x.DatabaseId, x.PrincipalId });
        principal.HasOne<DatabaseAccessConfiguration>().WithMany().HasForeignKey(x => x.DatabaseId)
            .OnDelete(DeleteBehavior.Restrict);
        principal.HasOne<DatabaseAccessSharedPrincipal>().WithMany().HasForeignKey(x => x.PrincipalId)
            .OnDelete(DeleteBehavior.Restrict);

        var role = model.Entity<DatabaseAccessDatabaseRole>();
        role.ToTable("DatabaseAccessDatabaseRole", "deploy");
        role.HasKey(x => new { x.DatabaseId, x.RoleId });
        role.HasOne<DatabaseAccessConfiguration>().WithMany().HasForeignKey(x => x.DatabaseId)
            .OnDelete(DeleteBehavior.Restrict);
        role.HasOne<DatabaseAccessSharedRole>().WithMany().HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        var membership = model.Entity<DatabaseAccessDatabaseMembership>();
        membership.ToTable("DatabaseAccessDatabaseMembership", "deploy");
        membership.HasKey(x => new { x.DatabaseId, x.PrincipalId, x.RoleId });
        membership.HasOne<DatabaseAccessDatabasePrincipal>().WithMany()
            .HasForeignKey(x => new { x.DatabaseId, x.PrincipalId }).OnDelete(DeleteBehavior.Restrict);
        membership.HasOne<DatabaseAccessDatabaseRole>().WithMany()
            .HasForeignKey(x => new { x.DatabaseId, x.RoleId }).OnDelete(DeleteBehavior.Restrict);

        var exclusion = model.Entity<DatabaseAccessExclusionRecord>();
        exclusion.ToTable("DatabaseAccessExclusion", "deploy");
        exclusion.HasKey(x => new { x.DatabaseId, x.LegacyUserId });
        exclusion.Property(x => x.LegacyUserId).ValueGeneratedNever();
        exclusion.HasOne<DatabaseAccessConfiguration>().WithMany().HasForeignKey(x => x.DatabaseId)
            .OnDelete(DeleteBehavior.Restrict);

        var audit = model.Entity<DatabaseAccessAudit>();
        audit.ToTable("DatabaseAccessAudit", "deploy");
        audit.HasKey(x => x.Id);
        audit.Property(x => x.Actor).HasMaxLength(512);
        audit.Property(x => x.Action).HasMaxLength(32);
        audit.Property(x => x.Status).HasMaxLength(32);
        audit.HasIndex(x => new { x.DatabaseId, x.CreatedUtc });
    }
}
