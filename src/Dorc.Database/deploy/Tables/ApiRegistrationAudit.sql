CREATE TABLE [deploy].[ApiRegistrationAudit] (
    [Id]                   BIGINT         IDENTITY (1, 1) NOT NULL,
    [ApiRegistrationId]    INT            NULL,
    [RefDataAuditActionId] INT            NOT NULL,
    [Username]             NVARCHAR (MAX) NOT NULL,
    [Date]                 DATETIME       NOT NULL,
    [FromValue]            NVARCHAR (MAX) NULL,
    [ToValue]              NVARCHAR (MAX) NULL,
    CONSTRAINT [PK_ApiRegistrationAudit] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_ApiRegistrationAudit_RefDataAuditAction] FOREIGN KEY ([RefDataAuditActionId]) REFERENCES [deploy].[RefDataAuditAction] ([RefDataAuditActionId])
);
