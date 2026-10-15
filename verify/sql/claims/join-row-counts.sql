-- Rows each join type returns on the seed: 4 customers (Dan has no orders), 8 orders (16 has no customer).
SELECT 'inner' AS JoinType, COUNT(*) AS Matches FROM Customers c INNER JOIN Orders o ON o.CustomerId = c.Id
UNION ALL
SELECT 'left', COUNT(*) FROM Customers c LEFT JOIN Orders o ON o.CustomerId = c.Id
UNION ALL
SELECT 'right', COUNT(*) FROM Customers c RIGHT JOIN Orders o ON o.CustomerId = c.Id
UNION ALL
SELECT 'full', COUNT(*) FROM Customers c FULL JOIN Orders o ON o.CustomerId = c.Id
UNION ALL
SELECT 'cross', COUNT(*) FROM Customers c CROSS JOIN Orders o;
