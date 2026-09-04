
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
ELSE IF EXISTS (SELECT 1 FROM deploy.SplitTagString(@TAGS) t WHERE LEN(t.Tag) > 400)
	BEGIN
		-- See usp_Insert_Database_Detail: rejected up front rather than silently
		-- dropped, so the tag rows deployments resolve against cannot diverge
		-- from the deprecated column.
		SELECT 'Tag exceeds 400 characters'
	END
ELSE
	BEGIN
		DECLARE @SERVER_ID INT

		-- See usp_Insert_Database_Detail: the row and its tag rows are one write,
		-- so a failure partway cannot leave the server tagged in the deprecated
		-- column and untagged in deploy.ServerTag.
		SET XACT_ABORT ON
		BEGIN TRANSACTION

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

		COMMIT TRANSACTION
	END

END TRY

BEGIN CATCH

IF XACT_STATE() <> 0 ROLLBACK TRANSACTION

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/