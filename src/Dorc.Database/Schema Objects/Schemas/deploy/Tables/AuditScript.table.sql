CREATE TABLE [deploy].[AuditScript]
(
    [Id] [bigint] IDENTITY(1,1) NOT NULL,
    [ScriptId] [bigint] NULL,
    [ScriptName] [varchar](64) NULL,
    [FromValue] [varchar](max) NULL,
    [ToValue] [varchar](max) NULL,
    [UpdatedBy] [varchar](100) NULL,
    [UpdatedDate] [datetime] NOT NULL,
    [Type] [varchar](100) NULL,
    [ProjectNames] [varchar](max) NULL
    )
