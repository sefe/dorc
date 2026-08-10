CREATE FUNCTION [deploy].[SplitTagString] (@value NVARCHAR(MAX))
RETURNS TABLE
AS
/*
 Split a legacy semicolon-separated tag string into rows: entries trimmed,
 empties dropped, duplicates removed.

 Tags are stored as rows in deploy.DatabaseTag / deploy.ServerTag. This exists for
 the two edges where a delimited string still arrives: the usp_Insert_*_Detail
 procedures, whose parameter shape is a contract with external operational
 tooling, and the one-time migration of the old column.

 A cascading-CTE tally rather than a recursive CTE, for two reasons. A recursive
 CTE would need OPTION (MAXRECURSION 0) to exceed 100 entries and that hint cannot
 be written inside an inline table-valued function, so a long list would have
 failed at run time rather than at build time. And a tally built from catalog
 views (sys.all_columns) does not resolve in the dacpac model, which has no system
 references - it would have failed the build. The constants below are
 self-contained and yield 65,536 positions, comfortably past the 4,000-character
 ceiling the old column had.

 DATALENGTH/2 rather than LEN for the bound, because LEN ignores trailing spaces
 and would drop a final entry that is nothing but padding - the exact class of
 value the old delimited column kept getting wrong.
*/
RETURN
(
    WITH N0 AS (SELECT 1 AS c UNION ALL SELECT 1),
         N1 AS (SELECT 1 AS c FROM N0 a CROSS JOIN N0 b),
         N2 AS (SELECT 1 AS c FROM N1 a CROSS JOIN N1 b),
         N3 AS (SELECT 1 AS c FROM N2 a CROSS JOIN N2 b),
         N4 AS (SELECT 1 AS c FROM N3 a CROSS JOIN N3 b),
         Positions AS
         (
             SELECT TOP (ISNULL(DATALENGTH(@value) / 2, 0))
                    ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
             FROM N4
         )
    SELECT DISTINCT
           LTRIM(RTRIM(SUBSTRING(@value, p.n, CHARINDEX(N';', @value + N';', p.n) - p.n))) AS Tag
    FROM Positions p
    WHERE (p.n = 1 OR SUBSTRING(@value, p.n - 1, 1) = N';')
      AND LTRIM(RTRIM(SUBSTRING(@value, p.n, CHARINDEX(N';', @value + N';', p.n) - p.n))) <> N''
);
