CREATE TABLE [deploy].[EnvironmentContainer] (
    [EnvId]       INT NOT NULL,
    [ContainerId] INT NOT NULL,
    CONSTRAINT [PK_EnvironmentContainer] PRIMARY KEY CLUSTERED ([EnvId] ASC, [ContainerId] ASC),
    CONSTRAINT [FK_EnvironmentContainer_Environment] FOREIGN KEY ([EnvId]) REFERENCES [deploy].[Environment] ([Id]),
    CONSTRAINT [FK_EnvironmentContainer_Container] FOREIGN KEY ([ContainerId]) REFERENCES [deploy].[Container] ([Id])
);
