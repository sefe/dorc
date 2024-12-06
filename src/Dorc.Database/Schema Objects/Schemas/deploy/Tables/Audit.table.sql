CREATE TABLE [deploy].[Audit](
	[Id] [bigint] IDENTITY(1,1) NOT NULL,
	[PropertyId] [bigint] NULL,
	[PropertyValueId] [bigint] NULL,
	[PropertyName] [varchar](64) NULL,
	[EnvironmentName] [varchar](64) NULL,
	[FromValue] [varchar](MAX) NULL,
	[ToValue] [varchar](MAX) NULL,
	[UpdatedBy] [varchar](100) NULL,
	[UpdatedDate] DATETIME NOT NULL,
	[Type] [varchar](100) NULL
);

