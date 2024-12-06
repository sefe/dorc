ALTER TABLE [deploy].[EnvironmentComponentStatus]
    ADD CONSTRAINT [FK_EnvironmentComponentStatus_Component] FOREIGN KEY ([ComponentId]) REFERENCES [deploy].[Component] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;

