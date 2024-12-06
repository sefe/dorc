using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class EnvironmentUserEntityTypeConfiguration : IEntityTypeConfiguration<EnvironmentUser>
    {
        public void Configure(EntityTypeBuilder<EnvironmentUser> builder)
        {
            builder
                .ToTable("ENVIRONMENT_USER_MAP")
                .HasKey(x => new { x.DbId, x.UserId, x.PermissionId });

            builder
                .Property(e => e.DbId)
                .HasColumnName("DB_ID")
                .HasColumnOrder(0);

            builder
                .Property(e => e.UserId)
                .HasColumnName("User_ID")
                .HasColumnOrder(1);

            builder
                .Property(e => e.PermissionId)
                .HasColumnName("Permission_ID")
                .HasColumnOrder(2);

            builder
                .HasOne(environmentUser => environmentUser.Database)
                .WithMany(database => database.EnvironmentUsers)
                .HasForeignKey(environmentUser => environmentUser.DbId)
                .IsRequired();

            builder
                .HasOne(environmentUser => environmentUser.User)
                .WithMany()
                .HasForeignKey(environmentUser => environmentUser.UserId)
                .IsRequired();

            builder
                .HasOne(environmentUser => environmentUser.Permission)
                .WithMany()
                .HasForeignKey(environmentUser => environmentUser.PermissionId)
                .IsRequired();

        }
    }
}
