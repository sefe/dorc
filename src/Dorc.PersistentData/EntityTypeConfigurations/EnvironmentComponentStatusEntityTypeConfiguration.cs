using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class EnvironmentComponentStatusEntityTypeConfiguration : IEntityTypeConfiguration<EnvironmentComponentStatus>
    {
        public void Configure(EntityTypeBuilder<EnvironmentComponentStatus> builder)
        {
            builder
                .ToTable("EnvironmentComponentStatus", "deploy")
                .HasKey(x => x.Id);

            builder
                .HasOne(x => x.Environment)
                .WithMany(x => x.ComponentStatus)
                .IsRequired()
                .HasForeignKey("EnvironmentId");

            builder
                .Property("EnvironmentId")
                .HasColumnName("EnvironmentId");

            builder
                .HasOne(x => x.Component)
                .WithMany()
                .IsRequired()
                .HasForeignKey("ComponentId");

            builder
                .Property("ComponentId")
                .HasColumnName("ComponentId");

            builder
                .HasOne(x => x.DeploymentRequest)
                .WithMany()
                .IsRequired()
                .HasForeignKey("DeploymentRequestId");

            builder
                .Property("DeploymentRequestId")
                .HasColumnName("DeploymentRequestId");
        }
    }
}
