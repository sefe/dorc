ALTER TABLE [deploy].[DeploymentResult]
    ADD CONSTRAINT [FK_DeploymentResult_Component] FOREIGN KEY ([ComponentId]) REFERENCES [deploy].[Component] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;

