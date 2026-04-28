using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class AuditScriptEntityTypeConfiguration : IEntityTypeConfiguration<AuditScript>
    {
        public void Configure(EntityTypeBuilder<AuditScript> builder)
        {
            builder
                .ToTable("AuditScript", "deploy")
                .HasKey(x => x.Id);

            builder
                .Property(audit => audit.ScriptName)
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