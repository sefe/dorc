CREATE TABLE [dbo].[KAFKA_ERROR_LOG] (
    [Id]               BIGINT             IDENTITY (1, 1) NOT NULL,
    [Topic]            NVARCHAR (255)     NOT NULL,
    [Partition]        INT                NOT NULL,
    [Offset]           BIGINT             NOT NULL,
    [ConsumerGroup]    NVARCHAR (255)     NOT NULL,
    [MessageKey]       NVARCHAR (512)     NULL,
    [RawPayload]       VARBINARY (MAX)    NULL,
    [PayloadTruncated] BIT                NOT NULL,
    [Error]            NVARCHAR (2000)    NOT NULL,
    [Stack]            NVARCHAR (MAX)     NULL,
    [OccurredAt]       DATETIMEOFFSET (7) NOT NULL,
    [LoggedAt]         DATETIMEOFFSET (7) NOT NULL,
    CONSTRAINT [KAFKA_ERROR_LOG_Id_pk] PRIMARY KEY CLUSTERED ([Id] ASC)
);
GO

CREATE NONCLUSTERED INDEX [IX_KAFKA_ERROR_LOG_Topic_OccurredAt]
    ON [dbo].[KAFKA_ERROR_LOG] ([Topic] ASC, [OccurredAt] DESC);
GO

CREATE NONCLUSTERED INDEX [IX_KAFKA_ERROR_LOG_ConsumerGroup_OccurredAt]
    ON [dbo].[KAFKA_ERROR_LOG] ([ConsumerGroup] ASC, [OccurredAt] DESC);
GO

CREATE NONCLUSTERED INDEX [IX_KAFKA_ERROR_LOG_OccurredAt]
    ON [dbo].[KAFKA_ERROR_LOG] ([OccurredAt] ASC);
GO
