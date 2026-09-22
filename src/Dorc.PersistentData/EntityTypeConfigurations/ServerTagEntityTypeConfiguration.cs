using Dorc.ApiModel;
using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class ServerTagEntityTypeConfiguration : IEntityTypeConfiguration<ServerTag>
    {
        public void Configure(EntityTypeBuilder<ServerTag> builder)
        {
            builder.ToTable("ServerTag", "deploy");
            builder.HasKey(e => new { e.ServerId, e.Tag });

            builder.Property(e => e.Tag).HasMaxLength(TagLimits.MaxTagLength).IsRequired();

            builder.HasIndex(e => e.Tag, "IX_ServerTag_Tag");

            builder.HasOne(e => e.Server)
                .WithMany(s => s.TagLinks)
                .HasForeignKey(e => e.ServerId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
