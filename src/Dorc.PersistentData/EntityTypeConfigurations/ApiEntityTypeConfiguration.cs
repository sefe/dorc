using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Environment = Dorc.PersistentData.Model.Environment;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class ApiEntityTypeConfiguration : IEntityTypeConfiguration<Api>
    {
        public void Configure(EntityTypeBuilder<Api> builder)
        {
            builder.ToTable("Api", "deploy");
            builder.HasKey(e => e.Id);

            builder.Property(e => e.Name).HasMaxLength(128).IsRequired();
            builder.Property(e => e.Endpoint).HasMaxLength(1024).IsRequired();
            builder.Property(e => e.Type).HasMaxLength(16).IsRequired();
            builder.Property(e => e.AuthType).HasMaxLength(16).IsRequired();
            builder.Property(e => e.HealthCheckPath).HasMaxLength(512);
            builder.Property(e => e.Tags).HasMaxLength(512);

            builder
                .HasOne(e => e.Environment)
                .WithMany(env => env.Apis)
                .HasForeignKey(e => e.EnvId)
                .OnDelete(DeleteBehavior.Cascade);

            builder
                .HasOne(e => e.OwnerProject)
                .WithMany()
                .HasForeignKey(e => e.OwnerProjectId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasIndex(e => new { e.EnvId, e.Name }, "UQ_Api_EnvId_Name").IsUnique();
            builder.HasIndex(e => e.EnvId, "IX_Api_EnvId");
        }
    }
}
