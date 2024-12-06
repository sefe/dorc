
-- =============================================
-- Author:		Martin Ives
-- Create date: 31st March 2016
-- Description:	Return a list of componnts for a named project
-- =============================================
CREATE PROCEDURE [deploy].[sp_Select_ProjectComponents] 
	-- Add the parameters for the stored procedure here
	@P_Name VARCHAR(50)
AS
BEGIN
	-- SET NOCOUNT ON added to prevent extra result sets from
	-- interfering with SELECT statements.
	SET NOCOUNT ON;

	DECLARE @ProjId   INT

	SET @ProjId = (SELECT TOP 1 P.Id FROM deploy.Project P WHERE P.Name = @P_Name)

	SELECT C.Id AS ComponentID
		  ,C.Name AS ComponentName
		  ,C.ParentId
		  ,S.Id AS ScriptId
		  ,S.Name AS ScriptName
		  ,S.Path AS ScriptPath

	  FROM deploy.Project P
	  INNER JOIN deploy.ProjectComponent PC ON PC.ProjectId = @ProjId
	  INNER JOIN deploy.Component C ON PC.ComponentId = C.Id
	  LEFT JOIN deploy.Script S ON S.Id = C.ScriptId
	  WHERE P.Id = @ProjId
	  ORDER BY C.ParentId, C.Name
END
GO
