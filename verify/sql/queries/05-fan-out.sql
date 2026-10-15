-- Revenue and items per customer: Ada's revenue comes out 330.00, not 230.00
SELECT c.Name, SUM(o.Total) AS Revenue, SUM(l.Quantity) AS Items
FROM Customers c
JOIN Orders o     ON o.CustomerId = c.Id
JOIN OrderLines l ON l.OrderId = o.Id
GROUP BY c.Name;

-- Fix: bring the lines to one row per order first, then join
SELECT c.Name, SUM(o.Total) AS Revenue, SUM(li.Items) AS Items
FROM Customers c
JOIN Orders o ON o.CustomerId = c.Id
JOIN (SELECT OrderId, SUM(Quantity) AS Items
      FROM OrderLines
      GROUP BY OrderId) li ON li.OrderId = o.Id
GROUP BY c.Name;
