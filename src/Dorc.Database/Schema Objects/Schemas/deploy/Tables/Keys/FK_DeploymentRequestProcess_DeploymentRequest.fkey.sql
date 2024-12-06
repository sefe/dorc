ALTER TABLE [deploy].[DeploymentRequestProcess]
	ADD CONSTRAINT [FK_DeploymentRequestProcess_DeploymentRequest]
	FOREIGN KEY ([DeploymentRequestId])
	REFERENCES [deploy].[DeploymentRequest] ([Id]) ON DELETE CASCADE ON UPDATE CASCADE;