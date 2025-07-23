CREATE TABLE [deploy].[ScriptAuditAction] (
    [ScriptAuditActionId] INT            IDENTITY (1, 1) NOT NULL,
    [Action]              NVARCHAR (MAX) NOT NULL,
    PRIMARY KEY CLUSTERED ([ScriptAuditActionId] ASC)
);