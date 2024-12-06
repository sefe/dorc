using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Environment = Dorc.PersistentData.Model.Environment;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class DatabaseEntityTypeConfiguration : IEntityTypeConfiguration<Database>
    {
        public void Configure(EntityTypeBuilder<Database> builder)
        {
            builder.HasKey(e => e.Id);
            builder.ToTable("DATABASE");

            builder.HasIndex(e => e.GroupId, "IX_DATABASE_Group_ID");

            builder.HasIndex(e => new { e.ServerName, e.Name }, "IX_DATABASE_Server_Name_DB_Name")
                .IsUnique()
                .HasFilter("([Server_Name] IS NOT NULL AND [DB_Name] IS NOT NULL)");

            builder.Property(e => e.Id).HasColumnName("DB_ID");
            builder.Property(e => e.ArrayName)
                .HasMaxLength(50)
                .HasColumnName("Array_Name");
            builder.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("DB_Name");
            builder.Property(e => e.Type)
                .HasMaxLength(50)
                .HasColumnName("DB_Type");
            builder.Property(e => e.GroupId).HasColumnName("Group_ID");
            builder.Property(e => e.ServerName)
                .HasMaxLength(50)
            .HasColumnName("Server_Name");

            builder.HasOne(d => d.Group).WithMany(p => p.Databases).HasForeignKey(d => d.GroupId);

            builder.HasMany(d => d.Environments).WithMany(p => p.Databases)
                .UsingEntity<Dictionary<string, object>>(
                    "EnvironmentDatabase",
                    r => r.HasOne<Environment>().WithMany().HasForeignKey("EnvId"),
                    l => l.HasOne<Database>().WithMany().HasForeignKey("DbId"),
                    j =>
                    {
                        j.HasKey("DbId", "EnvId");
                        j.ToTable("EnvironmentDatabase", "deploy");
                        j.HasIndex(new[] { "EnvId" }, "IX_EnvironmentDatabase_EnvId");
                    });
        }
    }
}
