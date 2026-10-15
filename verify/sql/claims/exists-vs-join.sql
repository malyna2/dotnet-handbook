-- Customers with a paid order. The JOIN repeats Ada once per paid order (5 rows); EXISTS and IN return each customer once (3 rows).
SELECT c.Name FROM Customers c JOIN Orders o ON o.CustomerId = c.Id WHERE o.Status = 'Paid';

SELECT c.Name FROM Customers c
WHERE EXISTS (SELECT 1 FROM Orders o WHERE o.CustomerId = c.Id AND o.Status = 'Paid');

SELECT c.Name FROM Customers c
WHERE c.Id IN (SELECT o.CustomerId FROM Orders o WHERE o.Status = 'Paid');
