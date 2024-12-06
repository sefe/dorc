using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class UserEntityTypeConfiguration : IEntityTypeConfiguration<User>
    {
        public void Configure(EntityTypeBuilder<User> builder)
        {
            builder
                .ToTable("USERS")
                .HasKey(user => user.Id);

            builder
                .Property(user => user.Id)
                .HasColumnName("User_ID");

            builder
                .Property(user => user.LoginId)
                .HasColumnName("Login_ID")
                .HasMaxLength(50);

            builder
                .Property(user => user.DisplayName)
                .HasColumnName("Display_Name")
                .HasMaxLength(50);

            builder
                .Property(user => user.Team)
                .HasMaxLength(50);

            builder
                .Property(user => user.LoginType)
                .HasColumnName("Login_Type")
                .HasMaxLength(50);

            builder
                .Property(user => user.LanId)
                .HasColumnName("LAN_ID")
                .HasMaxLength(50);

            builder
                .Property(user => user.LanIdType)
                .HasColumnName("LAN_ID_Type")
                .HasMaxLength(50);
        }
    }
}
