CREATE PROCEDURE [deploy].[sp_Select_Deployments_By_Project_Month]
AS
     SET NOCOUNT ON;
     DECLARE @err INT;
     TRUNCATE TABLE [deploy].[DeploymentsByProjectMonth]

     /*********************************************************************************************************/

    BEGIN TRY
        WITH 
            FixedProject AS (
                SELECT id,
                Name AS ProjectName
                FROM deploy.project),
            CombinedDeploymentRequests AS (
                 SELECT d.[id], d.[CompletedTime], d.[Project], d.[Status]
                 FROM [deploy].[DeploymentRequest] d
                 UNION ALL
                 SELECT ar.[id], ar.[CompletedTime], ar.[Project], ar.[Status]
                 FROM [archive].[DeploymentRequest] ar
             )
                 INSERT INTO [deploy].[DeploymentsByProjectMonth]
					SELECT	DATEPART(Year, [CompletedTime]) AS 'year',
							DATEPART(Month, [CompletedTime]) AS 'month',
							CASE WHEN p.Name is null THEN 'Unknown'
								ELSE p.Name
							END as ProjectName,
							COUNT(distinct dr.[id]) AS 'CountofDeployments',
							COUNT(distinct CASE WHEN dr.Status='Failed' THEN dr.[id] END) AS Failed
					FROM CombinedDeploymentRequests dr
							LEFT JOIN [deploy].Project p ON p.Name = dr.Project
             WHERE [CompletedTime] IS NOT NULL
             GROUP BY DATEPART(Year, [CompletedTime]),
                      DATEPART(Month, [CompletedTime]),
                      p.name 
					  ORDER BY year,
						month;
    END TRY
    BEGIN CATCH
        SELECT @err = @@ERROR;
        SELECT ERROR_MESSAGE() AS ErrorMessage;
    END CATCH;
GO
