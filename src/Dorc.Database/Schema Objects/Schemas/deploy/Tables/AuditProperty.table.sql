CREATE TABLE [deploy].[AuditProperty]
(
	[Id] [bigint] IDENTITY(1,1) NOT NULL,
	[PropertyId] [bigint] NULL,
	[PropertyName] [varchar](64) NULL,
	[FromValue] [varchar](max) NULL,
	[ToValue] [varchar](max) NULL,
	[UpdatedBy] [varchar](100) NULL,
	[UpdatedDate] [datetime] NOT NULL,
	[Type] [varchar](100) NULL
)
