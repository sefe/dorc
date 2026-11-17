using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Environment = Dorc.PersistentData.Model.Environment;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class EnvironmentEntityTypeConfiguration : IEntityTypeConfiguration<Environment>
    {
        public void Configure(EntityTypeBuilder<Environment> builder)
        {
            builder
                .ToTable("Environment", "deploy")
                .HasKey(x => x.Id);

            builder
                .Property(x => x.ObjectId)
                .IsRequired();

            builder
                .Property(e => e.ThinClientServer)
                .HasMaxLength(50);

            builder
                .HasMany(environment => environment.Histories)
                .WithOne(environmentHistory => environmentHistory.Environment)
                .HasForeignKey(environmentHistory => environmentHistory.EnvId);

            builder
                .HasMany(environment => environment.Databases)
                .WithMany(database => database.Environments)
                .UsingEntity(
                    configureRight => configureRight
                        .HasOne(typeof(Database))
                        .WithMany()
                        .HasForeignKey("DbId"),
                    configureLeft => configureLeft
                        .HasOne(typeof(Environment))
                        .WithMany()
                        .HasForeignKey("EnvId"),
                    configureJoinEntityType => configureJoinEntityType
                        .ToTable("EnvironmentDatabase", schema: "deploy"));

            builder
                .HasMany(e => e.Servers)
                .WithMany(e => e.Environments)
                .UsingEntity(
                    configureRight => configureRight.HasOne(typeof(Server))
                        .WithMany()
                        .HasForeignKey("ServerId"),
                    configureLeft => configureLeft.HasOne(typeof(Environment))
                        .WithMany()
                        .HasForeignKey("EnvId"),
                    configureJoinEntityType => configureJoinEntityType
                        .ToTable("EnvironmentServer", schema: "deploy"));

            builder
                .HasOne(e => e.ParentEnvironment)
                .WithMany(e => e.ChildEnvironments)
                .HasForeignKey("ParentId")
                .IsRequired(false);

            builder
                .Property(e => e.ParentId)
                .HasColumnName("ParentId");

            builder
                .Property(e => e.IsEphemeral)
                .HasDefaultValue(false);

            builder
                .Property(e => e.EnvironmentTier)
                .HasMaxLength(16);

            builder
                .Property(e => e.TemplateId);

            builder
                .Property(e => e.ExpiresAtUtc);

            builder
                .Property(e => e.EphemeralState)
                .HasMaxLength(32);

            builder
                .HasOne(e => e.Template)
                .WithMany(t => t.Environments)
                .HasForeignKey(e => e.TemplateId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            builder
                .HasMany(e => e.AccessControls)
                .WithOne()
                .HasPrincipalKey(e => e.ObjectId)
                .HasForeignKey("ObjectId");
        }
    }
}
