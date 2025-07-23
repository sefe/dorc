CREATE TABLE [deploy].[ScriptAudit] (
    [ScriptAuditId]       INT            IDENTITY (1, 1) NOT NULL,
    [ScriptId]            INT            NOT NULL,
    [ScriptAuditActionId] INT            NOT NULL,
    [Username]            NVARCHAR (MAX) NULL,
    [Date]                DATETIME       NOT NULL,
    [Json]                NVARCHAR (MAX) NULL,
    PRIMARY KEY CLUSTERED ([ScriptAuditId] ASC),
    CONSTRAINT [FK_Script_ScriptAudit] FOREIGN KEY ([ScriptId]) REFERENCES [deploy].[Script] ([Id]),
    CONSTRAINT [FK_ScriptAuditAction_ScriptAudit] FOREIGN KEY ([ScriptAuditActionId]) REFERENCES [deploy].[ScriptAuditAction] ([ScriptAuditActionId])
);