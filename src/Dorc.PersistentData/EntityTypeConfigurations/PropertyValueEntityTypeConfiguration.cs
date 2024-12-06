using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class PropertyValueEntityTypeConfiguration : IEntityTypeConfiguration<PropertyValue>
    {
        public void Configure(EntityTypeBuilder<PropertyValue> builder)
        {
            builder
                .ToTable("PropertyValue", "deploy")
                .HasKey(x => x.Id);

            builder
                .HasOne(x => x.Property)
                .WithMany(x => x.PropertyValues)
                .IsRequired()
                .HasForeignKey("PropertyId");

            builder
                .Property("PropertyId")
                .HasColumnName("PropertyId");
        }
    }
}
