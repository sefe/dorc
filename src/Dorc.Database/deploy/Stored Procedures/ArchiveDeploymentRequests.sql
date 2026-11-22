CREATE PROCEDURE [deploy].[ArchiveDeploymentRequests]
    @LastYear INT,
    @BatchSize INT = 1000  -- Default batch size
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

	DECLARE @TotalRequestsArchived INT = 0;
    DECLARE @RowCount INT = 1;
	DECLARE @BatchStartID INT = 0;

    -- Use a loop to process rows in batches
    WHILE @RowCount > 0
    BEGIN
        BEGIN TRY
            -- Temp table to store IDs for the batch
            DECLARE @BatchIDs TABLE (Id INT);

            INSERT INTO @BatchIDs (Id)
            SELECT d.Id
            FROM [deploy].[DeploymentRequest] d WITH (NOLOCK)
			LEFT JOIN [archive].[DeploymentRequest] a WITH (NOLOCK) ON d.Id = a.Id
            WHERE YEAR(d.RequestedTime) <= @LastYear
				AND d.Status IN ('Completed', 'Failed', 'Cancelled', 'Errored', 'Abandoned')
				AND a.Id IS NULL
				AND d.Id > @BatchStartID
            ORDER BY d.Id
			OFFSET 0 ROWS FETCH NEXT @BatchSize ROWS ONLY;

			SELECT @BatchStartID = ISNULL(MAX(Id), 0) FROM @BatchIDs;

			BEGIN TRANSACTION;
			-- Remove DeploymentRequestAttempt rows (DeploymentResultAttempt cascades from DeploymentRequestAttempt)
			DELETE FROM [deploy].[DeploymentRequestAttempt]
			WHERE DeploymentRequestId IN (SELECT Id FROM @BatchIDs);

			-- Remove execution receipts, Terraform outcomes/approvals, and catalog inputs/submissions for archived requests.
			-- Preserve rows in [deploy].[TerraformDeploymentReceipt] and [deploy].[DeploymentRequestDefinition] that act as
			-- the latest active Terraform backend-ownership evidence for a project/environment (checked by ProjectTerraformBackends.ValidateChange).
			DELETE FROM [deploy].[PowerShellExecutionReceipt]
			WHERE DeploymentRequestId IN (SELECT Id FROM @BatchIDs);

			DELETE FROM [deploy].[TerraformDeploymentOutcome]
			WHERE DeploymentResultId IN (
				SELECT Id FROM [deploy].[DeploymentResult]
				WHERE DeploymentRequestId IN (SELECT Id FROM @BatchIDs)
			);

			DELETE r
			FROM [deploy].[TerraformDeploymentReceipt] r
			WHERE r.DeploymentRequestId IN (SELECT Id FROM @BatchIDs)
			  AND (
			      r.TemplateName IS NULL
			      OR EXISTS (
			          SELECT 1
			          FROM [deploy].[TerraformDeploymentReceipt] newer
			          WHERE newer.DeploymentResultId > r.DeploymentResultId
			            AND ISNULL(newer.ProjectId, -1) = ISNULL(r.ProjectId, -1)
			            AND newer.ProjectName = r.ProjectName
			            AND ISNULL(newer.EnvironmentId, -1) = ISNULL(r.EnvironmentId, -1)
			            AND newer.EnvironmentName = r.EnvironmentName
			            AND newer.TemplateName IS NOT NULL
			            AND ISNULL(newer.StateStorageAccount, '') = ISNULL(r.StateStorageAccount, '')
			            AND ISNULL(newer.StateContainerName, '') = ISNULL(r.StateContainerName, '')
			            AND newer.DeploymentRequestId NOT IN (SELECT Id FROM @BatchIDs)
			      )
			  );

			DELETE FROM [deploy].[TerraformPlanApproval]
			WHERE DeploymentRequestId IN (SELECT Id FROM @BatchIDs);

			DELETE FROM [deploy].[CatalogRequestInputs]
			WHERE DeploymentRequestId IN (SELECT Id FROM @BatchIDs);

			DELETE FROM [deploy].[CatalogSubmission]
			WHERE DeploymentRequestId IN (SELECT Id FROM @BatchIDs);

			DELETE d
			FROM [deploy].[DeploymentRequestDefinition] d
			INNER JOIN [deploy].[DeploymentRequest] req ON req.Id = d.DeploymentRequestId
			WHERE d.DeploymentRequestId IN (SELECT Id FROM @BatchIDs)
			  AND (
			      d.IsSkipped = 1
			      OR req.TerraformBackendRetiredByChangeId IS NOT NULL
			      OR ISNULL(JSON_VALUE(d.[DefinitionJson], '$.TerraformSourceType'), '') NOT IN ('2', 'Catalog')
			      OR ISNULL(JSON_VALUE(d.[DefinitionJson], '$.ComponentType'), '') NOT IN ('1', 'Terraform')
			      OR EXISTS (
			          SELECT 1
			          FROM [deploy].[DeploymentRequestDefinition] newerDef
			          INNER JOIN [deploy].[DeploymentRequest] newerReq ON newerReq.Id = newerDef.DeploymentRequestId
			          WHERE newerDef.DeploymentRequestId > d.DeploymentRequestId
			            AND newerDef.ProjectId = d.ProjectId
			            AND newerReq.Environment = req.Environment
			            AND newerDef.IsSkipped = 0
			            AND newerReq.TerraformBackendRetiredByChangeId IS NULL
			            AND JSON_VALUE(newerDef.[DefinitionJson], '$.TerraformSourceType') IN ('2', 'Catalog')
			            AND JSON_VALUE(newerDef.[DefinitionJson], '$.ComponentType') IN ('1', 'Terraform')
			            AND newerDef.DeploymentRequestId NOT IN (SELECT Id FROM @BatchIDs)
			      )
			  );

			-- Archive EnvironmentComponentStatus data related to DeploymentRequest
            DELETE FROM [deploy].[EnvironmentComponentStatus]
            OUTPUT
                DELETED.[Id],
                DELETED.[EnvironmentId],
                DELETED.[ComponentId],
                DELETED.[Status],
                DELETED.[UpdateDate],
                DELETED.[DeploymentRequestId]
            INTO [archive].[EnvironmentComponentStatus] (
                [Id],
                [EnvironmentId],
                [ComponentId],
                [Status],
                [UpdateDate],
                [DeploymentRequestId]
            )
            WHERE DeploymentRequestId IN (SELECT Id FROM @BatchIDs);

            -- Archive DeploymentResult data related to DeploymentRequest
            DELETE FROM [deploy].[DeploymentResult]
            OUTPUT
                DELETED.[Id],
                DELETED.[DeploymentRequestId],
                DELETED.[ComponentId],
                DELETED.[Log],
                DELETED.[Status],
                DELETED.[StartedTime],
                DELETED.[CompletedTime]
            INTO [archive].[DeploymentResult] (
                [Id],
                [DeploymentRequestId],
                [ComponentId],
                [Log],
                [Status],
                [StartedTime],
                [CompletedTime]
            )
            WHERE DeploymentRequestId IN (SELECT Id FROM @BatchIDs);

            -- Delete from DeploymentRequest and output deleted rows into the archive
            DELETE FROM [deploy].[DeploymentRequest]
            OUTPUT
                DELETED.[Id],
                DELETED.[RequestDetails],
                DELETED.[UserName],
                DELETED.[RequestedTime],
                DELETED.[StartedTime],
                DELETED.[CompletedTime],
                DELETED.[Status],
                DELETED.[Log],
                DELETED.[IsProd],
                DELETED.[Project],
                DELETED.[Environment],
                DELETED.[BuildNumber],
                DELETED.[Components],
                DELETED.[UNCLogPath],
                DELETED.[CancelledBy],
                DELETED.[CancelledTime],
                DELETED.[EnvironmentOwnerEmail],
                DELETED.[TerraformBackendRetiredByChangeId]
            INTO [archive].[DeploymentRequest] (
                [Id],
                [RequestDetails],
                [UserName],
                [RequestedTime],
                [StartedTime],
                [CompletedTime],
                [Status],
                [Log],
                [IsProd],
                [Project],
                [Environment],
                [BuildNumber],
                [Components],
                [UNCLogPath],
                [CancelledBy],
                [CancelledTime],
                [EnvironmentOwnerEmail],
                [TerraformBackendRetiredByChangeId]
            )
            WHERE Id IN (SELECT Id FROM @BatchIDs);

            SET @RowCount = @@ROWCOUNT;
            SET @TotalRequestsArchived += @RowCount;

            COMMIT TRANSACTION;
        END TRY
        BEGIN CATCH
            -- Handle errors by rolling back any changes
            ROLLBACK TRANSACTION;
            -- Log the error or rethrow, or handle it as needed
            THROW;
        END CATCH

        -- Pause between batches to reduce load 
        WAITFOR DELAY '00:00:02'
    END

    SELECT @TotalRequestsArchived AS TotalDeploymentRequestsArchived;
END