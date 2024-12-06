

CREATE PROCEDURE [dbo].[usp_Select_Environment_Server_Map]
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
	SVR.Server_ID,
	SVR.Server_Name,
	SVC.Service_ID,
	SVC.Display_Name
FROM
	deploy.Environment ED,
	[SERVER] SVR,
	deploy.EnvironmentServer SVR_MAP,
	[SERVICE] SVC,
	SERVER_SERVICE_MAP SVC_MAP
WHERE
	ED.ID = SVR_MAP.EnvID
AND	SVR.Server_ID = SVR_MAP.ServerID
AND SVR.Server_ID = SVC_MAP.Server_ID
AND SVC.Service_ID = SVC_MAP.Service_ID
AND ED.ID = @ENV_ID

END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH



/************************************************************************************************************/

/*********************************************************************************************************/