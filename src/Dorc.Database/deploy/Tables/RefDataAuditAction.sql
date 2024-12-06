CREATE TABLE [deploy].[RefDataAuditAction] (
    [RefDataAuditActionId] INT            IDENTITY (1, 1) NOT NULL,
    [Action]               NVARCHAR (MAX) NOT NULL,
    PRIMARY KEY CLUSTERED ([RefDataAuditActionId] ASC)
);

