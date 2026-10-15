-- Meant: every customer, with their paid orders. Dan, who has none, disappears.
SELECT c.Name, o.Id AS OrderId
FROM Customers c
LEFT JOIN Orders o ON o.CustomerId = c.Id
WHERE o.Status = 'Paid';

-- Fix: a condition on the right table goes in ON. Dan comes back, with OrderId NULL.
SELECT c.Name, o.Id AS OrderId
FROM Customers c
LEFT JOIN Orders o ON o.CustomerId = c.Id AND o.Status = 'Paid';
