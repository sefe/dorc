using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class ComponentEntityTypeConfiguration : IEntityTypeConfiguration<Component>
    {
        public void Configure(EntityTypeBuilder<Component> builder)
        {
            builder
                .ToTable("Component", "deploy")
                .HasKey(x => x.Id);

            builder
                .HasOne(x => x.Script);

            builder
                .Property(x => x.ComponentType)
                .HasConversion<int>();

            builder
                .Property(x => x.TerraformSourceType)
                .HasConversion<int>();

            builder
                .HasOne(x => x.Parent)
                .WithMany(x => x.Children)
                .HasForeignKey("ParentId");

            builder
                .Property("ParentId")
                .HasColumnName("ParentId");

            builder
                .HasMany(s => s.Projects)
                .WithMany(c => c.Components)
                .UsingEntity(
                    configureRight => configureRight
                        .HasOne(typeof(Project))
                        .WithMany()
                        .HasForeignKey("ProjectId")
                        /*.HasPrincipalKey(nameof(Project.Id))*/,
                    configureLeft => configureLeft
                        .HasOne(typeof(Component))
                        .WithMany()
                        .HasForeignKey("ComponentId")
                        /*.HasPrincipalKey(nameof(Component.Id))*/,
                    configureJoinEntityType => configureJoinEntityType
                        .ToTable("ProjectComponent", schema: "deploy"));
        }
    }
}
