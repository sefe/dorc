using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class EnvironmentHistoryEntityTypeConfiguration : IEntityTypeConfiguration<EnvironmentHistory>
    {
        public void Configure(EntityTypeBuilder<EnvironmentHistory> builder)
        {
            builder
                .ToTable("EnvironmentHistory", "deploy")
                .HasKey(environmentHistory => environmentHistory.Id);

            builder
                .Property(e => e.UpdateType)
                .HasMaxLength(50);

            builder
                .Property(e => e.UpdatedBy)
                .HasMaxLength(50);

            builder
                .HasOne(environmentHistory => environmentHistory.Environment)
                .WithMany(environment => environment.Histories)
                .HasForeignKey(environmentHistory => environmentHistory.EnvId)
                .IsRequired();

            builder
                .Navigation(environmentHistory => environmentHistory.Environment)
                .IsRequired();
        }
    }
}
