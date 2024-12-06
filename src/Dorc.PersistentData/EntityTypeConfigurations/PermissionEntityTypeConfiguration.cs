using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class PermissionEntityTypeConfiguration : IEntityTypeConfiguration<Permission>
    {
        public void Configure(EntityTypeBuilder<Permission> builder)
        {
            builder
                .ToTable("PERMISSION")
                .HasKey(permission => permission.Id);

            builder
                .Property(permission => permission.Id)
                .HasColumnName("Permission_ID");

            builder
                .Property(permission => permission.Name)
                .HasColumnName("Permission_Name")
                .HasMaxLength(50);

            builder
                .Property(permission => permission.DisplayName)
                .HasColumnName("Display_Name")
                .HasMaxLength(50);
        }
    }
}
