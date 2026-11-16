SELECT c.Name, t.Id, t.Total
FROM Customers AS c
OUTER APPLY (
    SELECT TOP (1) o.Id, o.Total
    FROM Orders AS o
    WHERE o.CustomerId = c.Id
    ORDER BY o.CreatedAt DESC, o.Id DESC
) AS t;   -- every customer with their latest order; Dan with NULLs
