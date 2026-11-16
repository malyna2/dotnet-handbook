-- PostgreSQL's CROSS APPLY: CROSS JOIN LATERAL gives the same rows as queries/12-cross-apply.sql.
SELECT c.Name, t.Id, t.Total
FROM Customers AS c
CROSS JOIN LATERAL (
    SELECT o.Id, o.Total
    FROM Orders AS o
    WHERE o.CustomerId = c.Id
    ORDER BY o.Total DESC, o.Id
    LIMIT 3
) AS t;
