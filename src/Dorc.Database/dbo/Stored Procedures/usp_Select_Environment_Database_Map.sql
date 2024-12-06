

CREATE PROCEDURE [dbo].[usp_Select_Environment_Database_Map]
@ENV_ID int,
@DEBUG BIT

AS

SET NOCOUNT ON

DECLARE @err int

/*********************************************************************************************************/

IF @DEBUG = 0

BEGIN TRY

SELECT
	ED.ID,
	ED.Name,
	DB.DB_ID,
	DB.DB_Name
FROM
	deploy.Environment ED,
	[DATABASE] DB,
	deploy.EnvironmentDatabase MAP
WHERE
	ED.ID = MAP.EnvID
AND	DB.DB_ID = MAP.DBID
AND ED.ID = @ENV_ID

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/