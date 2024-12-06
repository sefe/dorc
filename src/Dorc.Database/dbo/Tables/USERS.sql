CREATE TABLE [dbo].[USERS] (
    [User_ID]      INT           IDENTITY (1, 1) NOT NULL,
    [Login_ID]     NCHAR (512)    NULL,
    [Display_Name] NVARCHAR (512) NULL,
    [Team]         NVARCHAR (512) NULL,
    [Login_Type]   NVARCHAR (512) NULL,
    [LAN_ID]       NVARCHAR (512) NULL,
    [LAN_ID_Type]  NVARCHAR (512) NULL,
    CONSTRAINT [PK_USERS] PRIMARY KEY CLUSTERED ([User_ID] ASC) WITH (DATA_COMPRESSION = PAGE)
);



