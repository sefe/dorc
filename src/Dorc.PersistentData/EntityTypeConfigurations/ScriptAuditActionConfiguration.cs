using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    internal class ScriptAuditActionConfiguration : IEntityTypeConfiguration<ScriptAuditAction>
    {
        public void Configure(EntityTypeBuilder<ScriptAuditAction> builder)
        {
            builder
                .ToTable("ScriptAuditAction", "deploy")
                .Property(p => p.Action)
                .HasConversion(new EnumToStringConverter<ActionType>());
        }
    }
}