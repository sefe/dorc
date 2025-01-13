IF COL_LENGTH('[deploy].[DeploymentResult]', 'StartedTime') IS NULL
BEGIN
    ALTER TABLE [deploy].[DeploymentResult]
    ADD [StartedTime] DATETIMEOFFSET (7) NULL;
END
IF COL_LENGTH('[deploy].[DeploymentResult]', 'CompletedTime') IS NULL
BEGIN
    ALTER TABLE [deploy].[DeploymentResult]
    ADD [CompletedTime] DATETIMEOFFSET (7) NULL;
END