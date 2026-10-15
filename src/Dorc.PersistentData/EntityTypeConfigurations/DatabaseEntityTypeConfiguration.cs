using Dorc.ApiModel;
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
            builder.ToTable("Database", "deploy");

            builder.HasIndex(e => e.GroupId, "IX_Database_GroupId");

            builder.HasIndex(e => new { e.ServerName, e.Name }, "IX_Database_ServerName_Name")
                .IsUnique()
                .HasFilter("([ServerName] IS NOT NULL AND [Name] IS NOT NULL)");

            builder.Property(e => e.ArrayName).HasMaxLength(50);
            builder.Property(e => e.Name).HasMaxLength(50);
            builder.Property(e => e.Tags).HasMaxLength(TagLimits.MaxTagStringLength);
            builder.Property(e => e.ServerName).HasMaxLength(50);

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
