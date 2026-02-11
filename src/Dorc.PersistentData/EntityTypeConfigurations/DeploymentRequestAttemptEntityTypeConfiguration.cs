using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class DeploymentRequestAttemptEntityTypeConfiguration : IEntityTypeConfiguration<DeploymentRequestAttempt>
    {
        public void Configure(EntityTypeBuilder<DeploymentRequestAttempt> builder)
        {
            builder
                .ToTable("DeploymentRequestAttempt", "deploy")
                .HasKey(x => x.Id);

            builder
                .HasOne(x => x.DeploymentRequest)
                .WithMany(x => x.DeploymentRequestAttempts)
                .IsRequired()
                .HasForeignKey("DeploymentRequestId");

            builder
                .Property(x => x.Status)
                .HasMaxLength(32)
                .IsRequired();

            builder
                .Property(x => x.UserName)
                .HasMaxLength(128)
                .IsRequired();

            builder
                .Property(x => x.AttemptNumber)
                .IsRequired();
        }
    }
}