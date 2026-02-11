using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class DeploymentResultAttemptEntityTypeConfiguration : IEntityTypeConfiguration<DeploymentResultAttempt>
    {
        public void Configure(EntityTypeBuilder<DeploymentResultAttempt> builder)
        {
            builder
                .ToTable("DeploymentResultAttempt", "deploy")
                .HasKey(x => x.Id);

            builder
                .HasOne(x => x.DeploymentRequestAttempt)
                .WithMany(x => x.DeploymentResultAttempts)
                .IsRequired()
                .HasForeignKey("DeploymentRequestAttemptId")
                .OnDelete(DeleteBehavior.Cascade);

            builder
                .Property(x => x.ComponentName)
                .HasMaxLength(256)
                .IsRequired();

            builder
                .Property(x => x.Status)
                .HasMaxLength(32)
                .IsRequired();

            builder
                .Property(x => x.ComponentId)
                .IsRequired();
        }
    }
}