CREATE PROCEDURE [deploy].[sp_Select_Deployments_By_Project]

AS

SET NOCOUNT ON

DECLARE @err int

/*********************************************************************************************************/

BEGIN TRY

;with FixedProject as 
(
  select id, case when ArtefactsSubPaths is null or ArtefactsSubPaths = '' 
                           then Name 
                           else ArtefactsSubPaths 
                           end as ProjectName
    from deploy.project
)
SELECT      
DatePart(Year, [CompletedTime]) as 'year',
DatePart(Month, [CompletedTime]) as 'month',
DatePart(Week, [CompletedTime]) as 'week',
ProjectName,
Count(dr.[id]) as 'Number of Deployments'
from [deploy].[DeploymentRequest] dr
cross apply RequestDetails.nodes('/DeploymentRequestDetail/Components/string') XMLData(sqlXML)
inner join [deploy].component c on c.name = sqlXML.value('.','varchar(200)')
inner join [deploy].[ProjectComponent] pc on pc.componentid = c.id
inner join [FixedProject] p on p.id = pc.projectid
  where [CompletedTime] is not NULL 
  GROUP BY DatePart(Year, [CompletedTime]), DatePart(Month, [CompletedTime]), DatePart(Week, [CompletedTime]),ProjectName 
  ORDER BY year,month, week


END TRY

BEGIN CATCH

SELECT @err = @@ERROR

SELECT ERROR_MESSAGE() AS ErrorMessage

END CATCH
GO
