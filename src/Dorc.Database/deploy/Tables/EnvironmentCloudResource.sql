CREATE TABLE [deploy].[EnvironmentCloudResource] (
    [EnvId]           INT NOT NULL,
    [CloudResourceId] INT NOT NULL,
    CONSTRAINT [PK_EnvironmentCloudResource] PRIMARY KEY CLUSTERED ([EnvId] ASC, [CloudResourceId] ASC),
    CONSTRAINT [FK_EnvironmentCloudResource_Environment] FOREIGN KEY ([EnvId]) REFERENCES [deploy].[Environment] ([Id]),
    CONSTRAINT [FK_EnvironmentCloudResource_CloudResource] FOREIGN KEY ([CloudResourceId]) REFERENCES [deploy].[CloudResource] ([Id])
);
