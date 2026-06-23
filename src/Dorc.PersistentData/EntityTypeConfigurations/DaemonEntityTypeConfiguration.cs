using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class DaemonEntityTypeConfiguration : IEntityTypeConfiguration<Daemon>
    {
        public void Configure(EntityTypeBuilder<Daemon> builder)
        {
            builder.ToTable("Daemon", "deploy");

            builder
                .Property(e => e.ServiceType)
                .HasColumnName("Type");
        }
    }
}
