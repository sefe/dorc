using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class ContainerAuditEntityTypeConfiguration : IEntityTypeConfiguration<ContainerAudit>
    {
        public void Configure(EntityTypeBuilder<ContainerAudit> builder)
        {
            builder.ToTable("ContainerAudit", "deploy");

            builder
                .HasOne(x => x.Action)
                .WithMany()
                .HasForeignKey(x => x.RefDataAuditActionId)
                .IsRequired();
        }
    }
}
