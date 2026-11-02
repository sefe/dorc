/*
 Pre-deploy check: could any existing tag be too long for the new Tag column?

 READ-ONLY. Run alongside AuditDatabaseTags.sql, BEFORE deploying.

 Tags move from a delimited column into rows in deploy.DatabaseTag /
 deploy.ServerTag, where Tag is NVARCHAR(400). The legacy columns were wider than
 that for a single value — dbo.DATABASE.DB_Type held 250 characters and
 dbo.SERVER.Application_Server_Name held 1000 — and in an estate where almost
 every value is one tag rather than a list, a single tag can be longer than the
 new column.

 The migration ABORTS the publish rather than truncating or skipping such a tag, so
 the failure mode is a stopped deploy and not lost data. This tells you in advance.

 The test is on the WHOLE value rather than on each split entry, deliberately: an
 individual tag can never be longer than the string that contains it, so this
 cannot miss one. It can over-report — a 500-character value that splits into four
 short tags is fine — which is the right direction for a pre-deploy check, and the
 row is right there to eyeball.

 Nothing returned means the migration has no length exposure.

 Schema-adaptive, like AuditDatabaseTags.sql: works before or after the
 reference-data move.
*/

SET NOCOUNT ON;

DECLARE @sql NVARCHAR(MAX), @table NVARCHAR(128), @id NVARCHAR(128),
        @name NVARCHAR(128), @tags NVARCHAR(128);

-- ---------- databases ----------
IF OBJECT_ID(N'deploy.[Database]', N'U') IS NOT NULL
    SELECT @table = N'[deploy].[Database]', @id = N'[Id]', @name = N'[Name]', @tags = N'[Tags]';
ELSE IF OBJECT_ID(N'dbo.[DATABASE]', N'U') IS NOT NULL
    SELECT @table = N'[dbo].[DATABASE]', @id = N'[DB_ID]', @name = N'[DB_Name]', @tags = N'[DB_Type]';
ELSE
    SET @table = NULL;

IF @table IS NOT NULL
BEGIN
    SET @sql = N'
    SELECT d.' + @id + N' AS DatabaseId,
           d.' + @name + N' AS DatabaseName,
           LEN(d.' + @tags + N') AS ValueLength,
           d.' + @tags + N' AS TagValue
    FROM ' + @table + N' d
    WHERE LEN(d.' + @tags + N') > 400
    ORDER BY LEN(d.' + @tags + N') DESC;';
    EXEC sp_executesql @sql;
END

-- ---------- servers (the legacy column allowed 1000, so the likelier half) ----------
IF OBJECT_ID(N'deploy.[Server]', N'U') IS NOT NULL
    SELECT @table = N'[deploy].[Server]', @id = N'[Id]', @name = N'[Name]', @tags = N'[Tags]';
ELSE IF OBJECT_ID(N'dbo.[SERVER]', N'U') IS NOT NULL
    SELECT @table = N'[dbo].[SERVER]', @id = N'[Server_ID]', @name = N'[Server_Name]', @tags = N'[Application_Server_Name]';
ELSE
    SET @table = NULL;

IF @table IS NOT NULL
BEGIN
    SET @sql = N'
    SELECT s.' + @id + N' AS ServerId,
           s.' + @name + N' AS ServerName,
           LEN(s.' + @tags + N') AS ValueLength,
           s.' + @tags + N' AS TagValue
    FROM ' + @table + N' s
    WHERE LEN(s.' + @tags + N') > 400
    ORDER BY LEN(s.' + @tags + N') DESC;';
    EXEC sp_executesql @sql;
END
