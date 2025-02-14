CREATE PROCEDURE [deploy].[ArchiveDeploymentRequests]
    @LastYear INT,
    @BatchSize INT = 1000  -- Default batch size
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

	DECLARE @MaxYearInArchive INT = 0;
	DECLARE @TotalRowsArchived INT = 0;
    DECLARE @RowCount INT = 1;
	DECLARE @BatchStartID INT = 0;

	SELECT @MaxYearInArchive = ISNULL(MAX(YEAR(RequestedTime)), 0)
	FROM [archive].[DeploymentRequest];

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
				AND YEAR(d.RequestedTime) >= @MaxYearInArchive
				AND a.Id IS NULL
				AND d.Id > @BatchStartID
            ORDER BY d.Id
			OFFSET 0 ROWS FETCH NEXT @BatchSize ROWS ONLY;

			SELECT @BatchStartID = ISNULL(MAX(Id), 0) FROM @BatchIDs;

			BEGIN TRANSACTION;
            -- Archive EnvironmentComponentStatus data related to DeploymentRequest
            DELETE FROM [deploy].[EnvironmentComponentStatus]
            OUTPUT DELETED.* INTO [archive].[EnvironmentComponentStatus]
            WHERE DeploymentRequestId IN (SELECT Id FROM @BatchIDs);

            -- Archive DeploymentResult data related to DeploymentRequest
            DELETE FROM [deploy].[DeploymentResult]
            OUTPUT DELETED.* INTO [archive].[DeploymentResult]
            WHERE DeploymentRequestId IN (SELECT Id FROM @BatchIDs);

            -- Delete from DeploymentRequest and output deleted rows into the archive
            DELETE FROM [deploy].[DeploymentRequest]
            OUTPUT DELETED.* INTO [archive].[DeploymentRequest]
            WHERE Id IN (SELECT Id FROM @BatchIDs);

            SET @RowCount = @@ROWCOUNT;
            SET @TotalRowsArchived += @RowCount;

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

    SELECT @TotalRowsArchived AS TotalRowsArchived;
END