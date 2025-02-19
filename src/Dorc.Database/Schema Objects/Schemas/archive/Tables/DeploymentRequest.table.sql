CREATE TABLE [archive].[DeploymentRequest]
(
    [Id]             INT                NOT NULL,
    [RequestDetails] XML                NOT NULL,
    [UserName]       NVARCHAR (128)     NOT NULL,
    [RequestedTime]  DATETIMEOFFSET (7) NULL,
    [StartedTime]    DATETIMEOFFSET (7) NULL,
    [CompletedTime]  DATETIMEOFFSET (7) NULL,
    [Status]         NVARCHAR (32)      NOT NULL,
    [Log]            NVARCHAR (MAX)     NULL,
    [IsProd]         BIT                NOT NULL,
    [Project]        NVARCHAR (64)      NULL,
    [Environment]    NVARCHAR (64)      NULL,
    [BuildNumber]    NVARCHAR (256)     NULL,
    [Components]     NVARCHAR (MAX)     NULL,
    [UNCLogPath]     NVARCHAR (1024)    NULL
)
