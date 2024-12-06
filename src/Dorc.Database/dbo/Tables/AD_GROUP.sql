﻿CREATE TABLE [dbo].[AD_GROUP] (
    [Group_ID]   INT           IDENTITY (1, 1) NOT NULL,
    [Group_Name] NVARCHAR (50) NULL,
    CONSTRAINT [AD_GROUP_Group_ID_pk] PRIMARY KEY CLUSTERED ([Group_ID] ASC) WITH (DATA_COMPRESSION = PAGE)
);



