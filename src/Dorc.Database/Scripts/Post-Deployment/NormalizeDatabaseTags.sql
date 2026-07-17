/*
 One-time normalization of dbo.[DATABASE].DB_Type (docs/database-tags, U-2).

 The tag-membership pattern (';' + DB_Type + ';' LIKE '%;<tag>;%') is exact about
 whitespace, while the old equality matching was forgiven trailing spaces by SQL's
 '=' — so every row is normalized once with the same rules the application applies
 on write: split on ';', trim each entry, drop empties, dedup exact duplicates
 keeping the first occurrence (binary comparison — same as the app's Ordinal),
 preserve order, re-join; a value with no surviving entries becomes NULL.

 Idempotent: an already-normalized value re-normalizes to itself and is skipped.
 Compat-100-safe: no STRING_SPLIT (unavailable below 130); manual CHARINDEX walk.
*/
DECLARE @DbId INT, @Raw NVARCHAR(4000);
DECLARE @Normalized NVARCHAR(4000), @Remaining NVARCHAR(4000), @Entry NVARCHAR(4000);
DECLARE @Pos INT;

DECLARE TagRows CURSOR LOCAL FAST_FORWARD FOR
    SELECT [DB_ID], [DB_Type]
    FROM [dbo].[DATABASE]
    WHERE [DB_Type] IS NOT NULL;

OPEN TagRows;
FETCH NEXT FROM TagRows INTO @DbId, @Raw;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @Normalized = NULL;
    SET @Remaining = @Raw;

    WHILE @Remaining IS NOT NULL
    BEGIN
        SET @Pos = CHARINDEX(';', @Remaining);
        IF @Pos > 0
        BEGIN
            SET @Entry = LEFT(@Remaining, @Pos - 1);
            SET @Remaining = SUBSTRING(@Remaining, @Pos + 1, 4000);
        END
        ELSE
        BEGIN
            SET @Entry = @Remaining;
            SET @Remaining = NULL;
        END

        SET @Entry = LTRIM(RTRIM(@Entry));

        -- Keep the first occurrence only; binary collation = exact (Ordinal) dedup.
        IF DATALENGTH(@Entry) > 0
           AND (@Normalized IS NULL
                OR CHARINDEX(';' + @Entry + ';' COLLATE Latin1_General_BIN2,
                             ';' + @Normalized + ';' COLLATE Latin1_General_BIN2) = 0)
        BEGIN
            SET @Normalized = CASE WHEN @Normalized IS NULL
                                   THEN @Entry
                                   ELSE @Normalized + ';' + @Entry END;
        END
    END

    IF @Normalized IS NULL
       OR @Normalized COLLATE Latin1_General_BIN2 <> @Raw COLLATE Latin1_General_BIN2
    BEGIN
        UPDATE [dbo].[DATABASE] SET [DB_Type] = @Normalized WHERE [DB_ID] = @DbId;
    END

    FETCH NEXT FROM TagRows INTO @DbId, @Raw;
END
CLOSE TagRows;
DEALLOCATE TagRows;
