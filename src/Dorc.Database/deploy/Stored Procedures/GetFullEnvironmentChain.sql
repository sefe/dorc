CREATE PROCEDURE [deploy].[GetFullEnvironmentChain]
    @EnvironmentId INT,
    @onlyParents BIT = 0
AS
BEGIN
    SET NOCOUNT ON;

    -- CTE to find all ancestors (parent chain)
    WITH Ancestors AS (
        SELECT Id, ParentId, Name, IsProd, Secure, Owner, ObjectId, 0 AS Distance
        FROM deploy.Environment
        WHERE Id = @EnvironmentId
        UNION ALL
        SELECT e.Id, e.ParentId, e.Name, e.IsProd, e.Secure, e.Owner, e.ObjectId, a.Distance + 1
        FROM deploy.Environment e
        INNER JOIN Ancestors a ON e.Id = a.ParentId
    ),
    -- CTE to find all descendants (child chain)
    Descendants AS (
        SELECT Id, ParentId, Name, IsProd, Secure, Owner, ObjectId, 0 AS Distance
        FROM deploy.Environment
        WHERE Id = @EnvironmentId
        UNION ALL
        SELECT e.Id, e.ParentId, e.Name, e.IsProd, e.Secure, e.Owner, e.ObjectId, d.Distance + 1
        FROM deploy.Environment e
        INNER JOIN Descendants d ON e.ParentId = d.Id AND @onlyParents = 0
    )
    -- Select and combine results from both CTEs
    -- Use DISTINCT to eliminate any duplicates
    SELECT DISTINCT Id, ParentId, Name, IsProd, Secure, Owner, ObjectId, Distance
    FROM (
        SELECT * FROM Ancestors
        UNION ALL
        SELECT * FROM Descendants
    ) AS CombinedResults
    ORDER BY Id;
END;
