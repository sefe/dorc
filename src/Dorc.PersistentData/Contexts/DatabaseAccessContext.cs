using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Contexts;

// No retry strategy: a lost lock/connection must not replay external database operations.
public sealed class DatabaseAccessContext(DbContextOptions<DatabaseAccessContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        var configuration = model.Entity<DatabaseAccessConfiguration>();
        configuration.ToTable("DatabaseAccessConfiguration");
        configuration.HasKey(x => x.DatabaseId);
        configuration.Property(x => x.DatabaseId).ValueGeneratedNever();
        configuration.Property(x => x.Provider).HasMaxLength(40);
        configuration.Property(x => x.Revision).IsConcurrencyToken();

        var principal = model.Entity<DatabaseAccessPrincipalRecord>();
        principal.ToTable("DatabaseAccessPrincipal");
        principal.HasKey(x => new { x.DatabaseId, x.Name });
        principal.Property(x => x.Name).HasMaxLength(128);
        principal.Property(x => x.Kind).HasMaxLength(32);
        principal.Property(x => x.LoginName).HasMaxLength(128);
        principal.Property(x => x.DirectoryId).HasMaxLength(256);
        principal.HasOne<DatabaseAccessConfiguration>().WithMany().HasForeignKey(x => x.DatabaseId)
            .OnDelete(DeleteBehavior.Restrict);

        var role = model.Entity<DatabaseAccessRoleRecord>();
        role.ToTable("DatabaseAccessRole");
        role.HasKey(x => new { x.DatabaseId, x.Name });
        role.Property(x => x.Name).HasMaxLength(128);
        role.HasOne<DatabaseAccessConfiguration>().WithMany().HasForeignKey(x => x.DatabaseId)
            .OnDelete(DeleteBehavior.Restrict);

        var membership = model.Entity<DatabaseAccessMembershipRecord>();
        membership.ToTable("DatabaseAccessMembership");
        membership.HasKey(x => new { x.DatabaseId, x.Principal, x.Role });
        membership.Property(x => x.Principal).HasMaxLength(128);
        membership.Property(x => x.Role).HasMaxLength(128);
        membership.HasOne<DatabaseAccessPrincipalRecord>().WithMany()
            .HasForeignKey(x => new { x.DatabaseId, x.Principal }).OnDelete(DeleteBehavior.Restrict);
        membership.HasOne<DatabaseAccessRoleRecord>().WithMany()
            .HasForeignKey(x => new { x.DatabaseId, x.Role }).OnDelete(DeleteBehavior.Restrict);

        var audit = model.Entity<DatabaseAccessAudit>();
        audit.ToTable("DatabaseAccessAudit");
        audit.HasKey(x => x.Id);
        audit.Property(x => x.Actor).HasMaxLength(512);
        audit.Property(x => x.Action).HasMaxLength(32);
        audit.Property(x => x.Status).HasMaxLength(32);
        audit.HasIndex(x => new { x.DatabaseId, x.CreatedUtc });
    }
}
