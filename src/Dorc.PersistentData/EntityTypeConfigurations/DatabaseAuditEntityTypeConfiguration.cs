using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class DatabaseAuditEntityTypeConfiguration : IEntityTypeConfiguration<DatabaseAudit>
    {
        public void Configure(EntityTypeBuilder<DatabaseAudit> builder)
        {
            builder.ToTable("DatabaseAudit", "deploy");

            builder
                .HasOne(x => x.Action)
                .WithMany()
                .HasForeignKey(x => x.RefDataAuditActionId)
                .IsRequired();
        }
    }
}
