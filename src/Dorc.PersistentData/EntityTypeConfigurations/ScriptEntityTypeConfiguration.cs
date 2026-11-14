using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class ScriptEntityTypeConfiguration : IEntityTypeConfiguration<Script>
    {
        public void Configure(EntityTypeBuilder<Script> builder)
        {
            builder
                .ToTable("Script", "deploy")
                .HasKey(x => x.Id);

            builder
                .Property(x => x.ContentHash)
                .HasMaxLength(64)
                .IsUnicode(false)
                .IsFixedLength();
        }
    }
}
