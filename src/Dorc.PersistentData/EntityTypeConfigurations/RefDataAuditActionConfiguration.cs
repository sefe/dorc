using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    internal class RefDataAuditActionConfiguration : IEntityTypeConfiguration<RefDataAuditAction>
    {
        public void Configure(EntityTypeBuilder<RefDataAuditAction> builder)
        {
            builder
                .ToTable("RefDataAuditAction", "deploy")
                .Property(p => p.Action)
                .HasConversion(new EnumToStringConverter<ActionType>());
        }
    }
}
