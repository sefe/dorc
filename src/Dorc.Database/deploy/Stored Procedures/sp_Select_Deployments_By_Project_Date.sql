CREATE PROCEDURE [deploy].[sp_Select_Deployments_By_Project_Date]
AS
     SET NOCOUNT ON;
     DECLARE @err INT;
	 TRUNCATE TABLE [deploy].[DeploymentsByProjectDate]

     /*********************************************************************************************************/

    BEGIN TRY
        WITH FixedProject
             AS (SELECT id,
                        Name AS ProjectName
                 FROM deploy.project)

				INSERT INTO [deploy].[DeploymentsByProjectDate]
					SELECT	DATEPART(Year, [CompletedTime]) AS 'year',
							DATEPART(Month, [CompletedTime]) AS 'month',
							DATEPART(Day, [CompletedTime]) AS 'day',
							CASE WHEN p.Name is null THEN 'Unknown'
								ELSE p.Name
							END as ProjectName,
							COUNT(distinct dr.[id]) AS 'CountofDeployments',
							COUNT(distinct CASE WHEN dr.Status='Failed' THEN dr.[id] END) AS Failed
					FROM [deploy].[DeploymentRequest] dr
						LEFT JOIN [deploy].Project p ON p.Name = dr.Project
					WHERE [CompletedTime] IS NOT NULL
							GROUP BY DATEPART(Year, [CompletedTime]),
							DATEPART(Month, [CompletedTime]),
							DATEPART(Day, [CompletedTime]),
							p.name 
					ORDER BY year,
							month,
							day;
    END TRY
    BEGIN CATCH
        SELECT @err = @@ERROR;
        SELECT ERROR_MESSAGE() AS ErrorMessage;
    END CATCH;