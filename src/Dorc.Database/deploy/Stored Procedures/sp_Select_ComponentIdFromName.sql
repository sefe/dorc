CREATE PROCEDURE [deploy].[sp_Select_ComponentIdFromName] 
	@P_Name VARCHAR(50)
AS
BEGIN
	SET NOCOUNT ON;

    IF EXISTS (SELECT TOP 1 Id FROM deploy.Component WHERE Name = @P_Name)
		BEGIN
			SELECT TOP 1 Id FROM deploy.Component WHERE Name = @P_Name
		END
	ELSE
		RETURN 1
END
GO

