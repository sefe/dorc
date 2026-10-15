using Dorc.ApiModel;
using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class DatabaseTagEntityTypeConfiguration : IEntityTypeConfiguration<DatabaseTag>
    {
        public void Configure(EntityTypeBuilder<DatabaseTag> builder)
        {
            builder.ToTable("DatabaseTag", "deploy");
            builder.HasKey(e => new { e.DatabaseId, e.Tag });

            builder.Property(e => e.Tag).HasMaxLength(TagLimits.MaxTagLength).IsRequired();

            builder.HasIndex(e => e.Tag, "IX_DatabaseTag_Tag");

            builder.HasOne(e => e.Database)
                .WithMany(d => d.TagLinks)
                .HasForeignKey(e => e.DatabaseId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
