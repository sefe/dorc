using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class ContainerEntityTypeConfiguration : IEntityTypeConfiguration<Container>
    {
        public void Configure(EntityTypeBuilder<Container> builder)
        {
            builder
                .ToTable("Container", "deploy")
                .HasKey(k => k.Id);

            builder.Property(e => e.Name).HasMaxLength(250).IsRequired();
            builder.Property(e => e.Image).HasMaxLength(500).IsRequired();
            builder.Property(e => e.Registry).HasMaxLength(250);
            builder.Property(e => e.HostServerName).HasMaxLength(250);
            builder.Property(e => e.Tags).HasMaxLength(250);

            builder.HasIndex(e => e.Name, "UQ_Container_Name").IsUnique();

            builder
                .HasMany(c => c.Environments)
                .WithMany(e => e.Containers)
                .UsingEntity(
                    j => j.HasOne(typeof(Model.Environment))
                        .WithMany()
                        .HasForeignKey("EnvId"),
                    j => j.HasOne(typeof(Container))
                        .WithMany()
                        .HasForeignKey("ContainerId"),
                    configureJoinEntityType =>
                    {
                        configureJoinEntityType.ToTable("EnvironmentContainer", "deploy");
                        configureJoinEntityType.HasKey("EnvId", "ContainerId");
                    });
        }
    }
}
