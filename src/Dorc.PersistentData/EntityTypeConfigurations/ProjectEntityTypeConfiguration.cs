using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using Environment = Dorc.PersistentData.Model.Environment;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class ProjectEntityTypeConfiguration : IEntityTypeConfiguration<Project>
    {
        public void Configure(EntityTypeBuilder<Project> builder)
        {
            builder
                .ToTable("Project", "deploy")
                .HasKey(x => x.Id);

            builder
                .Property(x => x.ObjectId)
                .ValueGeneratedOnAddOrUpdate();

            builder
                .Property(e => e.ArtefactsUrl)
                .HasMaxLength(512);

            builder
                .Property(e => e.ArtefactsSubPaths)
                .HasMaxLength(512);

            builder
                .Property(e => e.ArtefactsBuildRegex);

            builder
                .Property(e => e.TerraformGitRepoUrl)
                .HasMaxLength(512);

            builder
                .Property(x => x.SourceDatabaseId);

            builder
                .HasMany(x => x.Components)
                .WithMany(x => x.Projects)
                .UsingEntity(
                    configureRight => configureRight
                        .HasOne(typeof(Project))
                        .WithMany()
                        .HasForeignKey("ProjectId"),
                    configureLeft => configureLeft
                        .HasOne(typeof(Component))
                        .WithMany()
                        .HasForeignKey("ComponentId"),
                    configureJoinEntityType => configureJoinEntityType
                        .ToTable("ProjectComponent", schema: "deploy"));

            builder
                .HasMany(x => x.Environments)
                .WithMany(p => p.Projects)
                .UsingEntity(
                    configureRight => configureRight
                        .HasOne(typeof(Environment))
                        .WithMany()
                        .HasForeignKey("EnvironmentId"),
                    configureLeft => configureLeft
                        .HasOne(typeof(Project))
                        .WithMany()
                        .HasForeignKey("ProjectId"),
                    configureJoinEntityType => configureJoinEntityType
                        .ToTable("ProjectEnvironment", schema: "deploy"));
        }
    }
}
