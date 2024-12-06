using Dorc.ApiModel;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class BundledRequestsEntityTypeConfiguration : IEntityTypeConfiguration<BundledRequests>
    {
        public void Configure(EntityTypeBuilder<BundledRequests> builder)
        {
            builder
                .ToTable("BundledRequests", "deploy")
                .HasKey(bundledRequests => bundledRequests.Id);

            builder
                .Property(bundledRequests => bundledRequests.BundleName)
                .HasMaxLength(255);

            builder
                .Property(bundledRequests => bundledRequests.RequestName)
                .HasMaxLength(255);

            builder
                .Property(bundledRequests => bundledRequests.Type)
                .HasConversion(
                    v => v.ToString(),
                    v => (BundledRequestType)Enum.Parse(typeof(BundledRequestType), v));
        }
    }
}
