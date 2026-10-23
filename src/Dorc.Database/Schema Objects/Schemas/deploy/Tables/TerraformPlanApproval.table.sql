CREATE TABLE [deploy].[TerraformPlanApproval]
(
    [Id] INT IDENTITY (1, 1) NOT NULL,
    [DeploymentResultId] INT NOT NULL,
    [DeploymentRequestId] INT NOT NULL,
    [ApprovedBy] NVARCHAR (256) NOT NULL,
    [ApprovedAt] DATETIMEOFFSET (7) NOT NULL,
    [PlanSha256] VARCHAR (64) NOT NULL,
    [SourceSha256] VARCHAR (64) NOT NULL,
    [PlanContentSha256] VARCHAR (64) NOT NULL,
    CONSTRAINT [PK_TerraformPlanApproval] PRIMARY KEY CLUSTERED ([Id])
);
GO
CREATE INDEX [IX_TerraformPlanApproval_Result] ON [deploy].[TerraformPlanApproval] ([DeploymentResultId], [Id]);
