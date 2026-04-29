CREATE TABLE [deploy].[ServerAudit] (
    [Id]                   BIGINT         IDENTITY (1, 1) NOT NULL,
    [ServerId]             INT            NULL,
    [RefDataAuditActionId] INT            NOT NULL,
    [Username]             NVARCHAR (MAX) NOT NULL,
    [Date]                 DATETIME       NOT NULL,
    [FromValue]            NVARCHAR (MAX) NULL,
    [ToValue]              NVARCHAR (MAX) NULL,
    CONSTRAINT [PK_ServerAudit] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_ServerAudit_RefDataAuditAction] FOREIGN KEY ([RefDataAuditActionId]) REFERENCES [deploy].[RefDataAuditAction] ([RefDataAuditActionId])
);
