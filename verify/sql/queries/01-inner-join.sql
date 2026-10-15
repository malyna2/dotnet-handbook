SELECT o.Id, c.Name
FROM Orders o
INNER JOIN Customers c ON c.Id = o.CustomerId
WHERE o.Status = 'Open';
