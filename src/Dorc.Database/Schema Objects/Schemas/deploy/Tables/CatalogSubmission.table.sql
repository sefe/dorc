CREATE TABLE [deploy].[CatalogSubmission]
(
    [CallerHash] VARCHAR(64) NOT NULL,
    [SubmissionKey] UNIQUEIDENTIFIER NOT NULL,
    [PayloadSalt] VARBINARY(32) NOT NULL,
    [PayloadFingerprint] VARBINARY(32) NOT NULL,
    [DeploymentRequestId] INT NOT NULL,
    [ComponentJson] NVARCHAR(MAX) NOT NULL,
    CONSTRAINT [PK_CatalogSubmission] PRIMARY KEY ([CallerHash], [SubmissionKey])
);
