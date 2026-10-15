-- The SQL Server spelling of queries/08-recursive-cte.sql: plain WITH (SQL Server rejects WITH RECURSIVE).
WITH Tree AS (
    SELECT Id, Name, 0 AS Depth
    FROM Categories
    WHERE ParentId IS NULL
    UNION ALL
    SELECT c.Id, c.Name, t.Depth + 1
    FROM Categories c
    JOIN Tree t ON c.ParentId = t.Id
)
SELECT Id, Name, Depth FROM Tree;
