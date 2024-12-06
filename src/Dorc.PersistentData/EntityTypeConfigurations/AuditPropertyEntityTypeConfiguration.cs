using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class AuditPropertyEntityTypeConfiguration : IEntityTypeConfiguration<AuditProperty>
    {
        public void Configure(EntityTypeBuilder<AuditProperty> builder)
        {
            builder
                .ToTable("AuditProperty", "deploy")
                .HasKey(x => x.Id);

            builder
                .Property(audit => audit.PropertyName)
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
