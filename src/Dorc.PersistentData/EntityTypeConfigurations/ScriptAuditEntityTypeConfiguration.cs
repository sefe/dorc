using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class ScriptAuditEntityTypeConfiguration : IEntityTypeConfiguration<ScriptAudit>
    {
        public void Configure(EntityTypeBuilder<ScriptAudit> builder)
        {
            builder
                .ToTable("ScriptAudit", "deploy");

            builder
                .HasOne(x => x.Action)
                .WithMany(x => x.ScriptAudits)
                .HasForeignKey(x => x.RefDataAuditActionId)
                .IsRequired();

            builder
                .HasOne(x => x.Script)
                .WithMany()
                .HasForeignKey(x => x.ScriptId)
                .IsRequired()
                .OnDelete(DeleteBehavior.NoAction); // Preserve audit records when script is deleted
        }
    }
}