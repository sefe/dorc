CREATE TABLE [deploy].[ScriptAudit] (
    [ScriptAuditId]       INT            IDENTITY (1, 1) NOT NULL,
    [ScriptId]            INT            NOT NULL,
    [RefDataAuditActionId] INT            NOT NULL,
    [Username]            NVARCHAR (MAX) NULL,
    [Date]                DATETIME       NOT NULL,
    [Json]                NVARCHAR (MAX) NULL,
    PRIMARY KEY CLUSTERED ([ScriptAuditId] ASC),
    CONSTRAINT [FK_Script_ScriptAudit] FOREIGN KEY ([ScriptId]) REFERENCES [deploy].[Script] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_RefDataAuditAction_ScriptAudit] FOREIGN KEY ([RefDataAuditActionId]) REFERENCES [deploy].[RefDataAuditAction] ([RefDataAuditActionId])
);