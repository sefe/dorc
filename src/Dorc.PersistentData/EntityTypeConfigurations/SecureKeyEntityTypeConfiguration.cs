using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class SecureKeyEntityTypeConfiguration : IEntityTypeConfiguration<SecureKey>
    {
        public void Configure(EntityTypeBuilder<SecureKey> builder)
        {
            builder
                .ToTable("SecureKey", "deploy")
                .HasKey(t => t.Id);

            builder
                .Property(t => t.IV)
                .HasColumnName("IV")
                .IsRequired()
                .HasMaxLength(64);

            builder
                .Property(t => t.Key)
                .HasColumnName("Key")
                .IsRequired()
                .HasMaxLength(512);
        }
    }
}
