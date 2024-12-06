CREATE TABLE [deploy].[PropertyValueFilter] (
    [Id]               BIGINT         IDENTITY (1, 1) NOT NULL,
    [PropertyValueId]  BIGINT         NOT NULL,
    [PropertyFilterId] INT            NOT NULL,
    [Value]            NVARCHAR (MAX) NOT NULL
);

