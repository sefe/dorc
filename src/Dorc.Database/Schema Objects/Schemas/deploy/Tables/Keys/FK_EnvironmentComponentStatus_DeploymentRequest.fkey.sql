ALTER TABLE [deploy].[EnvironmentComponentStatus]
    ADD CONSTRAINT [FK_EnvironmentComponentStatus_DeploymentRequest] FOREIGN KEY ([DeploymentRequestId]) REFERENCES [deploy].[DeploymentRequest] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;

