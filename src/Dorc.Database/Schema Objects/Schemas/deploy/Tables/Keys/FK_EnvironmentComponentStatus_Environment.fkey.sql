ALTER TABLE [deploy].[EnvironmentComponentStatus]
    ADD CONSTRAINT [FK_EnvironmentComponentStatus_Environment] FOREIGN KEY ([EnvironmentId]) REFERENCES [deploy].[Environment] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;

