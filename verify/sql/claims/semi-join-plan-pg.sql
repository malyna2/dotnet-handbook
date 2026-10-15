-- IN and EXISTS get the same plan; NOT EXISTS an anti join; NOT IN a per-row filter over the hashed subquery (it must handle NULLs).
EXPLAIN (COSTS OFF) SELECT Name FROM Customers c WHERE c.Id IN (SELECT o.CustomerId FROM Orders o);
EXPLAIN (COSTS OFF) SELECT Name FROM Customers c WHERE EXISTS (SELECT 1 FROM Orders o WHERE o.CustomerId = c.Id);
EXPLAIN (COSTS OFF) SELECT Name FROM Customers c WHERE NOT EXISTS (SELECT 1 FROM Orders o WHERE o.CustomerId = c.Id);
EXPLAIN (COSTS OFF) SELECT Name FROM Customers c WHERE c.Id NOT IN (SELECT o.CustomerId FROM Orders o);
