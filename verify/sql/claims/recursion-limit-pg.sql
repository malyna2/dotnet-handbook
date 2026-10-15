-- PostgreSQL has no default recursion limit: the same 200 levels run.
WITH RECURSIVE N AS (
    SELECT 1 AS I
    UNION ALL
    SELECT I + 1 FROM N WHERE I < 200
)
SELECT MAX(I) AS Deepest FROM N;
