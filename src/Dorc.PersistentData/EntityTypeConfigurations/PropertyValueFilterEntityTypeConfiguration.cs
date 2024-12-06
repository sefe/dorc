using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class PropertyValueFilterEntityTypeConfiguration : IEntityTypeConfiguration<PropertyValueFilter>
    {
        public void Configure(EntityTypeBuilder<PropertyValueFilter> builder)
        {
            builder
                .ToTable("PropertyValueFilter", "deploy")
                .HasKey(x => x.Id);

            builder
                .HasOne(x => x.PropertyValue)
                .WithMany(x => x.Filters)
                .HasForeignKey("PropertyValueId")
                .IsRequired();

            builder
                .Property("PropertyValueId")
                .HasColumnName("PropertyValueId");

            builder
                .HasOne(x => x.PropertyFilter)
                .WithMany()
                .HasForeignKey("PropertyFilterId")
                .IsRequired();

            builder
                .Property("PropertyFilterId")
                .HasColumnName("PropertyFilterId");

        }
    }
}
