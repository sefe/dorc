

CREATE PROCEDURE [dbo].[usp_Insert_Database_Detail]
@DB_NAME NVARCHAR(50),
@TAGS NVARCHAR(4000),
@SERVER_NAME NVARCHAR(50),
@AD_GROUP int,
@DEBUG BIT

AS

SET NOCOUNT ON

DECLARE @err int

/*********************************************************************************************************/

IF @DEBUG = 0

BEGIN TRY

IF EXISTS (SELECT * FROM deploy.[Database] WHERE [Name] = @DB_NAME and [ServerName] = @SERVER_NAME)
	BEGIN
		SELECT 'Database Exists'
	END
ELSE
	BEGIN
		INSERT INTO deploy.[Database]
			([Name], [Tags], [ServerName], [GroupId])
		VALUES
			(@DB_NAME, @TAGS, @SERVER_NAME, @AD_GROUP)
	END

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/