using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class ServerEntityTypeConfiguration : IEntityTypeConfiguration<Server>
    {
        public void Configure(EntityTypeBuilder<Server> builder)
        {
            builder
                .ToTable("SERVER")
                .HasKey(k => k.Id);

            builder
                .Property(e => e.Id)
                .HasColumnName("Server_ID");

            builder
                .Property(e => e.Name)
                .HasColumnName("Server_Name")
                .HasMaxLength(32);

            builder
                .Property(e => e.OsName)
                .HasColumnName("OS_Version")
                .HasMaxLength(50);

            builder
                .Property(e => e.ApplicationTags)
                .HasColumnName("Application_Server_Name")
                .HasMaxLength(250);

            builder
                .HasMany(s => s.Services)
                .WithMany(d => d.Server)
                .UsingEntity("SERVER_SERVICE_MAP",
                    j => j.HasOne(typeof(Daemon))
                        .WithMany()
                        .HasForeignKey("Service_ID"),
                    j => j.HasOne(typeof(Server))
                        .WithMany()
                        .HasForeignKey("Server_ID"));

            builder
                .HasMany(s => s.Environments)
                .WithMany(e => e.Servers)
                .UsingEntity(
                    j => j.HasOne(typeof(Model.Environment))
                        .WithMany()
                        .HasForeignKey("EnvId"),
                    j => j.HasOne(typeof(Server))
                        .WithMany()
                        .HasForeignKey("ServerId"),
                    configureJoinEntityType => configureJoinEntityType
                        .ToTable("EnvironmentServer", schema: "deploy"));
        }
    }
}
