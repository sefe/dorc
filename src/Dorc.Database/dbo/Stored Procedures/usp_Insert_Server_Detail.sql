
CREATE PROCEDURE [dbo].[usp_Insert_Server_Detail]
@SERVER_NAME NVARCHAR(50),
@OS_VERSION NVARCHAR(50),
@TAGS NVARCHAR(4000),
@DEBUG BIT

AS

SET NOCOUNT ON

DECLARE @err int

/*********************************************************************************************************/

IF @DEBUG = 0

BEGIN TRY

IF EXISTS (SELECT * FROM deploy.[Server] WHERE [Name] = @SERVER_NAME)
	BEGIN
		SELECT 'Server Exists'
	END
ELSE
	BEGIN
		INSERT INTO deploy.[Server]
			([Name], [OsName], [Tags])
		VALUES
			(@SERVER_NAME, @OS_VERSION, @TAGS)
	END

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/