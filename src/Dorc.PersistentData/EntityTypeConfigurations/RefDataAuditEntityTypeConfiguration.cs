using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Reflection.Emit;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class RefDataAuditEntityTypeConfiguration : IEntityTypeConfiguration<RefDataAudit>
    {
        public void Configure(EntityTypeBuilder<RefDataAudit> builder)
        {
            builder
                .ToTable("RefDataAudit", "deploy");

            builder
                .HasOne(x => x.Action)
                .WithMany(x => x.RefDataAudits)
                .HasForeignKey(x => x.RefDataAuditActionId)
                .IsRequired();

            builder
                .HasOne(x => x.Project)
                .WithMany(x => x.RefDataAudits)
                .HasForeignKey(x => x.ProjectId)
                .IsRequired();
        }
    }
}
