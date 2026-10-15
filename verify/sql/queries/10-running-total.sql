SELECT Id, CreatedAt, Total,
       SUM(Total) OVER (ORDER BY CreatedAt)              AS ByDate,   -- 100 200 200 230
       SUM(Total) OVER (ORDER BY CreatedAt, Id
                        ROWS BETWEEN UNBOUNDED PRECEDING
                                 AND CURRENT ROW)       AS Running   -- 100 150 200 230
FROM Orders
WHERE CustomerId = 1;
