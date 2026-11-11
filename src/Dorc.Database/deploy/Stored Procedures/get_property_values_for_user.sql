CREATE PROCEDURE [deploy].[get_property_values_for_user] 
    @env varchar(256) = NULL,
    @prop nvarchar(512) = NULL,
    @username varchar(256), 
    @sidList varchar(MAX)
AS
BEGIN
    SELECT DISTINCT 
        p.Name,
        p.Secure,
        p.IsArray,
        pv.Value,
        pvf.Value as PropertyFilterValue,
        CASE 
            WHEN @prop IS NOT NULL THEN pv.Id 
            ELSE NULL 
        END AS PropertyValueId,
        CASE 
            WHEN @prop IS NOT NULL THEN pvf.Id 
            ELSE NULL 
        END AS PropertyValueFilterId,
        CASE
            WHEN (e1.Owner = @username AND NOT EXISTS (
                SELECT 1
                FROM deploy.AccessControl acDeny
                INNER JOIN STRING_SPLIT(@sidList, ';') denySids ON (denySids.value = acDeny.Sid OR denySids.value = acDeny.Pid)
                WHERE acDeny.ObjectId = e1.ObjectId AND acDeny.Deny != 0
            )) OR EXISTS (
                SELECT e.Id
                FROM deploy.environment e
                INNER JOIN deploy.AccessControl ac ON ac.ObjectId = e.ObjectId
                INNER JOIN STRING_SPLIT(@sidList, ';') sids ON (sids.value = ac.Sid OR sids.value = ac.Pid)
                WHERE e.Name = e1.Name
                  AND (ac.Allow & 4) != 0
                  AND ac.Deny = 0
                  AND NOT EXISTS (
                      SELECT 1
                      FROM deploy.AccessControl acDeny
                      INNER JOIN STRING_SPLIT(@sidList, ';') denySids ON (denySids.value = acDeny.Sid OR denySids.value = acDeny.Pid)
                      WHERE acDeny.ObjectId = e.ObjectId AND acDeny.Deny != 0
                  )
            ) THEN 1
            ELSE 0
        END AS IsOwner,
        0 AS IsDelegate,
        CASE
            WHEN EXISTS (
                SELECT e.Id
                FROM deploy.environment e
                INNER JOIN deploy.Environment ed ON e.Name = ed.Name
                INNER JOIN deploy.AccessControl ac ON ac.ObjectId = e.ObjectId
                INNER JOIN STRING_SPLIT(@sidList, ';') sids ON (sids.value = ac.Sid OR sids.value = ac.Pid)
                WHERE e.Name = e1.Name
                  AND ((ac.Allow & 1) != 0 OR (ac.Allow & 4) != 0)
                  AND ac.Deny = 0
                  AND NOT EXISTS (
                      SELECT 1
                      FROM deploy.AccessControl acDeny
                      INNER JOIN STRING_SPLIT(@sidList, ';') denySids ON (denySids.value = acDeny.Sid OR denySids.value = acDeny.Pid)
                      WHERE acDeny.ObjectId = e.ObjectId AND acDeny.Deny != 0
                  )
            ) THEN 1
            ELSE 0
        END AS IsPermissioned,
        pf.Priority
    FROM [deploy].[Property] AS p
    INNER JOIN [deploy].[PropertyValue] AS pv ON pv.PropertyId = p.Id
    LEFT JOIN [deploy].[PropertyValueFilter] AS pvf ON pvf.PropertyValueId = pv.Id
    INNER JOIN [deploy].[PropertyFilter] AS pf ON pf.Id = pvf.PropertyFilterId
    LEFT JOIN deploy.environment e1 ON pvf.Value = e1.Name
    WHERE 
        (@env IS NULL OR pvf.Value = @env)  -- Filter by environment if @env is provided
        AND (@prop IS NULL OR p.Name = @prop) -- Filter by property name if @prop is provided
    ORDER BY p.Name;
END