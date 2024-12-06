using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class DeploymentResultEntityTypeConfiguration : IEntityTypeConfiguration<DeploymentResult>
    {
        public void Configure(EntityTypeBuilder<DeploymentResult> builder)
        {
            builder
                .ToTable("DeploymentResult", "deploy")
                .HasKey(x => x.Id);

            builder
                .HasOne(x => x.DeploymentRequest)
                .WithMany(x => x.DeploymentResults)
                .IsRequired()
                .HasForeignKey("DeploymentRequestId");

            builder
                .Property(deploymentResult => deploymentResult.Status)
                .HasMaxLength(32);

            builder
                .HasOne(x => x.Component)
                .WithMany()
                .IsRequired()
                .HasForeignKey("ComponentId");

            builder
                .Property("ComponentId")
                .HasColumnName("ComponentId");
        }
    }
}
