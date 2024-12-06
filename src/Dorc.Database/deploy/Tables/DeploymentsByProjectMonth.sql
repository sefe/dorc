CREATE TABLE [deploy].[DeploymentsByProjectMonth] (
[Id]       INT            IDENTITY (1, 1) NOT NULL,
    [year]			   INT            NOT NULL,
    [month]			   INT            NOT NULL,
    [ProjectName]           NVARCHAR (MAX) NOT NULL,
    [CountOfDeployments]    INT		    NOT NULL,
    [Failed]                INT		    NOT NULL
);

