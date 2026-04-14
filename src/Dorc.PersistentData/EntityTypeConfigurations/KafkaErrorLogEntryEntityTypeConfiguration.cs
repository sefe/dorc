using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dorc.PersistentData.EntityTypeConfigurations
{
    public class KafkaErrorLogEntryEntityTypeConfiguration : IEntityTypeConfiguration<KafkaErrorLogEntry>
    {
        public void Configure(EntityTypeBuilder<KafkaErrorLogEntry> builder)
        {
            builder
                .ToTable("KAFKA_ERROR_LOG")
                .HasKey(x => x.Id);

            builder.Property(e => e.Id).HasColumnName("Id");

            builder.Property(e => e.Topic)
                .HasColumnName("Topic")
                .HasMaxLength(255)
                .IsRequired();

            builder.Property(e => e.Partition)
                .HasColumnName("Partition")
                .IsRequired();

            builder.Property(e => e.Offset)
                .HasColumnName("Offset")
                .IsRequired();

            builder.Property(e => e.ConsumerGroup)
                .HasColumnName("ConsumerGroup")
                .HasMaxLength(255)
                .IsRequired();

            builder.Property(e => e.MessageKey)
                .HasColumnName("MessageKey")
                .HasMaxLength(512);

            builder.Property(e => e.RawPayload)
                .HasColumnName("RawPayload")
                .HasColumnType("varbinary(max)");

            builder.Property(e => e.PayloadTruncated)
                .HasColumnName("PayloadTruncated")
                .IsRequired();

            builder.Property(e => e.Error)
                .HasColumnName("Error")
                .HasMaxLength(2000)
                .IsRequired();

            builder.Property(e => e.Stack)
                .HasColumnName("Stack")
                .HasColumnType("nvarchar(max)");

            builder.Property(e => e.OccurredAt)
                .HasColumnName("OccurredAt")
                .HasColumnType("datetimeoffset(7)")
                .IsRequired();

            builder.Property(e => e.LoggedAt)
                .HasColumnName("LoggedAt")
                .HasColumnType("datetimeoffset(7)")
                .IsRequired();

            builder.HasIndex(e => new { e.Topic, e.OccurredAt })
                .HasDatabaseName("IX_KAFKA_ERROR_LOG_Topic_OccurredAt")
                .IsDescending(false, true);

            builder.HasIndex(e => new { e.ConsumerGroup, e.OccurredAt })
                .HasDatabaseName("IX_KAFKA_ERROR_LOG_ConsumerGroup_OccurredAt")
                .IsDescending(false, true);

            builder.HasIndex(e => e.OccurredAt)
                .HasDatabaseName("IX_KAFKA_ERROR_LOG_OccurredAt");
        }
    }
}
