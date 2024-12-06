ALTER TABLE [deploy].[DeploymentResult]
    ADD CONSTRAINT [FK_DeploymentResult_DeploymentRequest] FOREIGN KEY ([DeploymentRequestId]) REFERENCES [deploy].[DeploymentRequest] ([Id]) ON DELETE CASCADE ON UPDATE CASCADE;



