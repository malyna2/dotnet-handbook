-- SQL Server stops a recursive CTE after 100 levels by default (error 530); OPTION (MAXRECURSION n) changes it.
WITH N AS (
    SELECT 1 AS I
    UNION ALL
    SELECT I + 1 FROM N WHERE I < 200
)
SELECT MAX(I) AS Deepest FROM N;
