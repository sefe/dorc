using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class AdGroupEntityTypeConfiguration : IEntityTypeConfiguration<AdGroup>
    {
        public void Configure(EntityTypeBuilder<AdGroup> builder)
        {
            builder
                .ToTable("AD_GROUP")
                .HasKey(x => x.Id);

            builder
                .Property(e => e.Id)
                .HasColumnName("Group_ID");

            builder
                .Property(e => e.Name)
                .HasColumnName("Group_Name")
                .HasMaxLength(50)
                .IsRequired();
        }
    }
}
