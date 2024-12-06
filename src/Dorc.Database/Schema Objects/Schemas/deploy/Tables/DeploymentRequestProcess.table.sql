CREATE TABLE [deploy].[DeploymentRequestProcess]
(
	[Id]				  INT                IDENTITY (1, 1) NOT NULL,
	[DeploymentRequestId] INT                NOT NULL,
	[ProcessId]			  INT                NOT NULL
	CONSTRAINT [PK_DeploymentRequestProcess] PRIMARY KEY CLUSTERED ([Id] ASC)
);

GO