CREATE TABLE [deploy].[RefDataAudit] (
    [RefDataAuditId]       INT            IDENTITY (1, 1) NOT NULL,
    [ProjectId]            INT            NULL,
    [RefDataAuditActionId] INT            NOT NULL,
    [Username]             NVARCHAR (MAX) NOT NULL,
    [Date]                 DATETIME       NOT NULL,
    [Json]                 NVARCHAR (MAX) NOT NULL,
    PRIMARY KEY CLUSTERED ([RefDataAuditId] ASC),
    CONSTRAINT [FK_Project_RefDataAudit] FOREIGN KEY ([ProjectId]) REFERENCES [deploy].Project ON DELETE SET NULL,
    CONSTRAINT [FK_RefDataAuditAction_RefDataAudit] FOREIGN KEY ([RefDataAuditActionId]) REFERENCES [deploy].[RefDataAuditAction] ([RefDataAuditActionId])
);

