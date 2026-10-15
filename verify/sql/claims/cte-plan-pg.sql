-- PostgreSQL 12+: a CTE referenced once is inlined (no CTE Scan); referenced twice, it is computed once (CTE Scan).
EXPLAIN (COSTS OFF)
WITH Paid AS (SELECT CustomerId, Total FROM Orders WHERE Status = 'Paid')
SELECT * FROM Paid WHERE CustomerId = 1;

EXPLAIN (COSTS OFF)
WITH Paid AS (SELECT CustomerId, Total FROM Orders WHERE Status = 'Paid')
SELECT * FROM Paid p1 JOIN Paid p2 ON p2.CustomerId = p1.CustomerId;
