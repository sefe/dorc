CREATE TABLE [dbo].[PERMISSION] (
    [Permission_ID]   INT           IDENTITY (1, 1) NOT NULL,
    [Permission_Name] NVARCHAR (50) NULL,
    [Display_Name]    NVARCHAR (50) NULL,
    CONSTRAINT [PERMISSION_Permission_ID_pk] PRIMARY KEY CLUSTERED ([Permission_ID] ASC) WITH (DATA_COMPRESSION = PAGE)
);



