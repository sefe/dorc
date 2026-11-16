CREATE TABLE [deploy].[ContainerAudit] (
    [Id]                   BIGINT         IDENTITY (1, 1) NOT NULL,
    [ContainerId]          INT            NULL,
    [RefDataAuditActionId] INT            NOT NULL,
    [Username]             NVARCHAR (MAX) NOT NULL,
    [Date]                 DATETIME       NOT NULL,
    [FromValue]            NVARCHAR (MAX) NULL,
    [ToValue]              NVARCHAR (MAX) NULL,
    CONSTRAINT [PK_ContainerAudit] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_ContainerAudit_RefDataAuditAction] FOREIGN KEY ([RefDataAuditActionId]) REFERENCES [deploy].[RefDataAuditAction] ([RefDataAuditActionId])
);
