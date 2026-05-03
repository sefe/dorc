using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class DaemonAuditEntityTypeConfiguration : IEntityTypeConfiguration<DaemonAudit>
    {
        public void Configure(EntityTypeBuilder<DaemonAudit> builder)
        {
            builder.ToTable("DaemonAudit", "deploy");

            builder
                .HasOne(x => x.Action)
                .WithMany()
                .HasForeignKey(x => x.RefDataAuditActionId)
                .IsRequired();
        }
    }
}
