SELECT c.Name,
       COUNT(*)     AS AllRows,   -- Dan: 1, his row of NULLs counts
       COUNT(o.Id)  AS Orders,    -- Dan: 0, NULLs are skipped
       SUM(o.Total) AS Revenue    -- Dan: NULL, not 0
FROM Customers c
LEFT JOIN Orders o ON o.CustomerId = c.Id
GROUP BY c.Name;
