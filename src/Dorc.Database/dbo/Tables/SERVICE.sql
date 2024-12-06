CREATE TABLE [dbo].[SERVICE] (
    [Service_ID]   INT           IDENTITY (1, 1) NOT NULL,
    [Service_Name] NVARCHAR (250) NULL,
    [Display_Name] NVARCHAR (250) NULL,
    [Account_Name] NVARCHAR (250) NULL,
    [Service_Type] NVARCHAR (250) NULL,
    CONSTRAINT [PK_SERVICE] PRIMARY KEY CLUSTERED ([Service_ID] ASC) WITH (DATA_COMPRESSION = PAGE)
);

