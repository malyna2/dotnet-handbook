SELECT c.Name, t.Id, t.Total
FROM Customers AS c
CROSS APPLY (
    SELECT TOP (3) o.Id, o.Total
    FROM Orders AS o
    WHERE o.CustomerId = c.Id        -- the inner query sees the outer row
    ORDER BY o.Total DESC, o.Id
) AS t;   -- the three largest orders of each customer; Dan has none, so no row
