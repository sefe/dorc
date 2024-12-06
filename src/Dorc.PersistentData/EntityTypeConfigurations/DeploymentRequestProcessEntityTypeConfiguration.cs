using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class DeploymentRequestProcessEntityTypeConfiguration : IEntityTypeConfiguration<DeploymentRequestProcess>
    {
        public void Configure(EntityTypeBuilder<DeploymentRequestProcess> builder)
        {
            builder
                .ToTable("DeploymentRequestProcess", "deploy");

            #region Shadow Primary Key
            builder
                .Property<int>("Id")
                .HasColumnType("int")
                .UseIdentityColumn();

            builder
                .HasKey("Id");
            #endregion

            builder
                .HasOne(deploymentRequestProcess => deploymentRequestProcess.DeploymentRequest)
                .WithMany(deploymentRequest => deploymentRequest.DeploymentRequestProcesses)
                .IsRequired()
                .HasForeignKey("DeploymentRequestId");
        }
    }
}
