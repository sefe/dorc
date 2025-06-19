IF NOT EXISTS (SELECT * FROM [deploy].[AccessControl] WHERE [Allow] = 4)
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;

        -- Populate AccessControl table with Environment owners
        INSERT INTO [deploy].[AccessControl] ([ObjectId], [Name], [Sid], [Allow], [Deny], [Pid])
        SELECT 
            e.[ObjectId],
            e.[Owner] as [Name],
            e.[Owner] as [Sid],
            4 as [Allow], -- Owner Access control
            0 as [Deny],
            null as [Pid]
        FROM [deploy].[Environment] e
        WHERE e.[Owner] IS NOT NULL
        AND NOT EXISTS (
            SELECT 1 
            FROM [deploy].[AccessControl] ac 
            WHERE ac.[ObjectId] = e.[ObjectId] 
            AND ac.[Allow] = 4
        );

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        
        DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
        DECLARE @ErrorSeverity INT = ERROR_SEVERITY();
        DECLARE @ErrorState INT = ERROR_STATE();
        
        RAISERROR (@ErrorMessage, @ErrorSeverity, @ErrorState);
    END CATCH;
END; 