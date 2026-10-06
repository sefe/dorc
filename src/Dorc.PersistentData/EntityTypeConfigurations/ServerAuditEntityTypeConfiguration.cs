using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class ServerAuditEntityTypeConfiguration : IEntityTypeConfiguration<ServerAudit>
    {
        public void Configure(EntityTypeBuilder<ServerAudit> builder)
        {
            builder.ToTable("ServerAudit", "deploy");

            builder
                .HasOne(x => x.Action)
                .WithMany()
                .HasForeignKey(x => x.RefDataAuditActionId)
                .IsRequired();
        }
    }
}
