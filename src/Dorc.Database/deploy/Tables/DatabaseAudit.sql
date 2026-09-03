CREATE TABLE [deploy].[DatabaseAudit] (
    [Id]                   BIGINT         IDENTITY (1, 1) NOT NULL,
    [DatabaseId]           INT            NULL,
    [RefDataAuditActionId] INT            NOT NULL,
    [Username]             NVARCHAR (MAX) NOT NULL,
    [Date]                 DATETIME       NOT NULL,
    [FromValue]            NVARCHAR (MAX) NULL,
    [ToValue]              NVARCHAR (MAX) NULL,
    CONSTRAINT [PK_DatabaseAudit] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_DatabaseAudit_RefDataAuditAction] FOREIGN KEY ([RefDataAuditActionId]) REFERENCES [deploy].[RefDataAuditAction] ([RefDataAuditActionId])
);
