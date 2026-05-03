using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class DaemonObservationEntityTypeConfiguration : IEntityTypeConfiguration<DaemonObservation>
    {
        public void Configure(EntityTypeBuilder<DaemonObservation> builder)
        {
            builder.ToTable("DaemonObservation", "deploy");
            builder.HasKey(x => x.Id);
        }
    }
}
