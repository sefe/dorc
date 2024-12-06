

CREATE PROCEDURE [dbo].[usp_Update_Database_Detail]
@DB_ID int,
@DB_NAME NVARCHAR(50),
@DB_TYPE NVARCHAR(50),
@SERVER_NAME NVARCHAR(50),
@AD_GROUP int,
@DEBUG BIT

AS

SET NOCOUNT ON

DECLARE @err int

/*********************************************************************************************************/

IF @DEBUG = 0

BEGIN TRY

UPDATE dbo.[DATABASE]
SET	DB_Name = @DB_NAME, 
	DB_Type = @DB_TYPE, 
	Server_Name = @SERVER_NAME,
	Group_ID = @AD_GROUP
WHERE
	DB_ID = @DB_ID

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/