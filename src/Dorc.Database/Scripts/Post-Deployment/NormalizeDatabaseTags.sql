/*
 One-time normalization of deploy.[Database].Tags (docs/database-tags, U-2).

 The tag-membership pattern (';' + Tags + ';' LIKE '%;<tag>;%') is exact about
 whitespace, while the old equality matching was forgiven trailing spaces by SQL's
 '=' — so every row is normalized once with the rules the application applies on
 write: split on ';', trim each entry, drop empties, dedup exact duplicates
 keeping the first occurrence (binary comparison — same as the app's Ordinal),
 preserve order, re-join; a value with no surviving entries becomes NULL.

 Scope note: LTRIM/RTRIM trim SPACES only, which is exactly the class of padding
 SQL '=' used to forgive (its padding rules are space-only too). Entries padded
 with other whitespace (tab/CR/LF) never matched the old equality either, are not
 a regression, and converge the next time the application rewrites the row
 (TagString.Normalize trims all .NET whitespace).

 Idempotent: an already-normalized value re-normalizes to itself and is skipped,
 and rows that cannot need work are filtered out before the cursor opens.
 Compat-100-safe: no STRING_SPLIT (unavailable below 130); manual CHARINDEX walk.
*/
DECLARE @DbId INT, @Raw NVARCHAR(4000);
DECLARE @Normalized NVARCHAR(4000), @Remaining NVARCHAR(4000), @Entry NVARCHAR(4000);
DECLARE @Pos INT;

DECLARE TagRows CURSOR LOCAL FAST_FORWARD FOR
    SELECT [Id], [Tags]
    FROM [deploy].[Database]
    -- Only rows that could possibly need work. A value with no whitespace and no
    -- delimiter already normalizes to itself, so skipping it here costs nothing and
    -- keeps this cursor from re-walking the whole table on every future publish.
    WHERE [Tags] IS NOT NULL
      AND ([Tags] LIKE '%;%' OR [Tags] <> LTRIM(RTRIM([Tags])));

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
        UPDATE [deploy].[Database] SET [Tags] = @Normalized WHERE [Id] = @DbId;
    END

    FETCH NEXT FROM TagRows INTO @DbId, @Raw;
END
CLOSE TagRows;
DEALLOCATE TagRows;
