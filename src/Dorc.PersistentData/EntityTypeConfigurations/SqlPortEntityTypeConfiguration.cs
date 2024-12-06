using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class SqlPortEntityTypeConfiguration : IEntityTypeConfiguration<SqlPort>
    {
        public void Configure(EntityTypeBuilder<SqlPort> builder)
        {
            builder
                .ToTable("SQL_PORTS", "dbo")
                .HasKey(p => new { p.Instance_Name, p.SQL_Port });

            builder
                .Property(e => e.Instance_Name)
                .HasMaxLength(250);

            builder
                .Property(e => e.SQL_Port)
                .HasMaxLength(10)
                .IsFixedLength();
        }
    }
}
