CREATE TABLE [deploy].[DaemonAudit] (
    [Id]                   BIGINT         IDENTITY (1, 1) NOT NULL,
    [DaemonId]             INT            NULL,
    [RefDataAuditActionId] INT            NOT NULL,
    [Username]             NVARCHAR (MAX) NOT NULL,
    [Date]                 DATETIME       NOT NULL,
    [FromValue]            NVARCHAR (MAX) NULL,
    [ToValue]              NVARCHAR (MAX) NULL,
    CONSTRAINT [PK_DaemonAudit] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_DaemonAudit_RefDataAuditAction] FOREIGN KEY ([RefDataAuditActionId]) REFERENCES [deploy].[RefDataAuditAction] ([RefDataAuditActionId])
);
