using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class ApiRegistrationEntityTypeConfiguration : IEntityTypeConfiguration<ApiRegistration>
    {
        public void Configure(EntityTypeBuilder<ApiRegistration> builder)
        {
            builder
                .ToTable("ApiRegistration", "deploy")
                .HasKey(k => k.Id);

            builder.Property(e => e.Name).HasMaxLength(250).IsRequired();
            builder.Property(e => e.BaseUrl).HasMaxLength(500).IsRequired();
            builder.Property(e => e.Version).HasMaxLength(50);
            builder.Property(e => e.HealthCheckUrl).HasMaxLength(500);
            builder.Property(e => e.Tags).HasMaxLength(250);

            builder.HasIndex(e => e.Name, "UQ_ApiRegistration_Name").IsUnique();

            builder
                .HasMany(c => c.Environments)
                .WithMany(e => e.ApiRegistrations)
                .UsingEntity(
                    j => j.HasOne(typeof(Model.Environment))
                        .WithMany()
                        .HasForeignKey("EnvId"),
                    j => j.HasOne(typeof(ApiRegistration))
                        .WithMany()
                        .HasForeignKey("ApiRegistrationId"),
                    configureJoinEntityType =>
                    {
                        configureJoinEntityType.ToTable("EnvironmentApiRegistration", "deploy");
                        configureJoinEntityType.HasKey("EnvId", "ApiRegistrationId");
                    });
        }
    }
}
