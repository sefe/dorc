using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class CloudResourceEntityTypeConfiguration : IEntityTypeConfiguration<CloudResource>
    {
        public void Configure(EntityTypeBuilder<CloudResource> builder)
        {
            builder
                .ToTable("CloudResource", "deploy")
                .HasKey(k => k.Id);

            builder.Property(e => e.Name).HasMaxLength(250).IsRequired();
            builder.Property(e => e.Provider).HasMaxLength(250).IsRequired();
            builder.Property(e => e.ResourceType).HasMaxLength(250).IsRequired();
            builder.Property(e => e.ResourceIdentifier).HasMaxLength(500).IsRequired();
            builder.Property(e => e.Subscription).HasMaxLength(250);
            builder.Property(e => e.Tags).HasMaxLength(250);

            builder.HasIndex(e => e.Name, "UQ_CloudResource_Name").IsUnique();

            builder
                .HasMany(c => c.Environments)
                .WithMany(e => e.CloudResources)
                .UsingEntity(
                    j => j.HasOne(typeof(Model.Environment))
                        .WithMany()
                        .HasForeignKey("EnvId"),
                    j => j.HasOne(typeof(CloudResource))
                        .WithMany()
                        .HasForeignKey("CloudResourceId"),
                    configureJoinEntityType =>
                    {
                        configureJoinEntityType.ToTable("EnvironmentCloudResource", "deploy");
                        configureJoinEntityType.HasKey("EnvId", "CloudResourceId");
                    });
        }
    }
}
