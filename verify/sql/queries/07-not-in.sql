-- Meant: customers who never ordered. Returns no rows: guest order 16 has CustomerId NULL.
SELECT Name FROM Customers
WHERE Id NOT IN (SELECT CustomerId FROM Orders);

-- Dan
SELECT Name FROM Customers c
WHERE NOT EXISTS (SELECT 1 FROM Orders o WHERE o.CustomerId = c.Id);
