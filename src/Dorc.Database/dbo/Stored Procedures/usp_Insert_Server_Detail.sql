
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
		DECLARE @SERVER_ID INT

		INSERT INTO deploy.[Server]
			([Name], [OsName], [Tags])
		VALUES
			(@SERVER_NAME, @OS_VERSION, @TAGS)

		SET @SERVER_ID = SCOPE_IDENTITY()

		-- @TAGS keeps its delimited shape: it is a contract with external
		-- operational tooling. deploy.ServerTag is the source of truth, so it is
		-- split on the way in; the deprecated column above is dual-written until
		-- the follow-up release drops it.
		INSERT INTO deploy.[ServerTag] ([ServerId], [Tag])
		SELECT @SERVER_ID, t.Tag
		FROM deploy.SplitTagString(@TAGS) t
		WHERE LEN(t.Tag) <= 200
	END

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/