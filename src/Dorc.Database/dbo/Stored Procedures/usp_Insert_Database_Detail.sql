

CREATE PROCEDURE [dbo].[usp_Insert_Database_Detail]
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

IF EXISTS (SELECT * FROM dbo.[DATABASE] WHERE DB_Name = @DB_NAME and Server_Name = @SERVER_NAME)
	BEGIN
		SELECT 'Database Exists'
	END
ELSE
	BEGIN
		INSERT INTO dbo.[DATABASE]
			(DB_Name, DB_Type, Server_Name, Group_ID)
		VALUES
			(@DB_NAME, @DB_TYPE, @SERVER_NAME, @AD_GROUP)
	END

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/