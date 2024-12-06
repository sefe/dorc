using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class AuditEntityTypeConfiguration : IEntityTypeConfiguration<Audit>
    {
        public void Configure(EntityTypeBuilder<Audit> builder)
        {
            builder
                .ToTable("Audit", "deploy")
                .HasKey(audit => audit.Id);

            builder
                .Property(audit => audit.PropertyName)
                .HasMaxLength(64);

            builder
                .Property(audit => audit.EnvironmentName)
                .HasMaxLength(64);

            builder
                .Property(audit => audit.UpdatedBy)
                .HasMaxLength(100);

            builder
                .Property(audit => audit.Type)
                .HasMaxLength(100);
        }
    }
}
