CREATE TABLE [deploy].[CloudResourceAudit] (
    [Id]                   BIGINT         IDENTITY (1, 1) NOT NULL,
    [CloudResourceId]      INT            NULL,
    [RefDataAuditActionId] INT            NOT NULL,
    [Username]             NVARCHAR (MAX) NOT NULL,
    [Date]                 DATETIME       NOT NULL,
    [FromValue]            NVARCHAR (MAX) NULL,
    [ToValue]              NVARCHAR (MAX) NULL,
    CONSTRAINT [PK_CloudResourceAudit] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_CloudResourceAudit_RefDataAuditAction] FOREIGN KEY ([RefDataAuditActionId]) REFERENCES [deploy].[RefDataAuditAction] ([RefDataAuditActionId])
);
