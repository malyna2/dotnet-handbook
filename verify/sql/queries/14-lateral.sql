SELECT c.Name, t.Id, t.Total
FROM Customers AS c
LEFT JOIN LATERAL (
    SELECT o.Id, o.Total
    FROM Orders AS o
    WHERE o.CustomerId = c.Id
    ORDER BY o.CreatedAt DESC, o.Id DESC
    LIMIT 1
) AS t ON true;   -- PostgreSQL's OUTER APPLY
