-- DISTINCT is not the fix: Ada's orders 11 and 12 are both 50.00, so SUM(DISTINCT) gives 180.00 instead of 230.00.
-- COUNT(DISTINCT o.Id) does count her orders right (4).
SELECT c.Name, SUM(DISTINCT o.Total) AS Revenue, COUNT(DISTINCT o.Id) AS Orders, SUM(o.Total) AS Inflated
FROM Customers c
JOIN Orders o     ON o.CustomerId = c.Id
JOIN OrderLines l ON l.OrderId = o.Id
WHERE c.Id = 1
GROUP BY c.Name;
