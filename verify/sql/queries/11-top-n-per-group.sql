WITH Ranked AS (
    SELECT CustomerId, Id, Total,
           ROW_NUMBER() OVER (PARTITION BY CustomerId
                              ORDER BY Total DESC, Id) AS Rn
    FROM Orders
)
SELECT CustomerId, Id, Total
FROM Ranked
WHERE Rn <= 2;   -- the two largest orders of each customer
