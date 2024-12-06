using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class DaemonEntityTypeConfiguration : IEntityTypeConfiguration<Daemon>
    {
        public void Configure(EntityTypeBuilder<Daemon> builder)
        {
            builder
            .ToTable("SERVICE");

            builder
                .Property(e => e.Id)
                .HasColumnName("Service_ID");
            builder
                .Property(e => e.Name)
                .HasColumnName("Service_Name");
            builder
                .Property(e => e.DisplayName)
                .HasColumnName("Display_Name");
            builder
                .Property(e => e.AccountName)
                .HasColumnName("Account_Name");
            builder
                .Property(e => e.ServiceType)
                .HasColumnName("Service_Type");
        }
    }
}
