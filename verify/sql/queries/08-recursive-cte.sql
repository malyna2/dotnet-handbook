WITH RECURSIVE Tree AS (          -- SQL Server: plain WITH
    SELECT Id, Name, 0 AS Depth
    FROM Categories
    WHERE ParentId IS NULL           -- anchor: the roots
    UNION ALL
    SELECT c.Id, c.Name, t.Depth + 1
    FROM Categories c
    JOIN Tree t ON c.ParentId = t.Id -- step: the children of the rows found so far
)
SELECT Id, Name, Depth FROM Tree;
