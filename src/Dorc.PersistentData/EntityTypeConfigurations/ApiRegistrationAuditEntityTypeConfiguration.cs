using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class ApiRegistrationAuditEntityTypeConfiguration : IEntityTypeConfiguration<ApiRegistrationAudit>
    {
        public void Configure(EntityTypeBuilder<ApiRegistrationAudit> builder)
        {
            builder.ToTable("ApiRegistrationAudit", "deploy");

            builder
                .HasOne(x => x.Action)
                .WithMany()
                .HasForeignKey(x => x.RefDataAuditActionId)
                .IsRequired();
        }
    }
}
