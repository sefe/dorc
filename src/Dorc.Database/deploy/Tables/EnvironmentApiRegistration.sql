CREATE TABLE [deploy].[EnvironmentApiRegistration] (
    [EnvId]             INT NOT NULL,
    [ApiRegistrationId] INT NOT NULL,
    CONSTRAINT [PK_EnvironmentApiRegistration] PRIMARY KEY CLUSTERED ([EnvId] ASC, [ApiRegistrationId] ASC),
    CONSTRAINT [FK_EnvironmentApiRegistration_Environment] FOREIGN KEY ([EnvId]) REFERENCES [deploy].[Environment] ([Id]),
    CONSTRAINT [FK_EnvironmentApiRegistration_ApiRegistration] FOREIGN KEY ([ApiRegistrationId]) REFERENCES [deploy].[ApiRegistration] ([Id])
);
