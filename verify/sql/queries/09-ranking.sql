SELECT Id, Total,
       ROW_NUMBER() OVER (ORDER BY Total DESC, Id) AS RowNumber,  -- 1 2 3 4
       RANK()       OVER (ORDER BY Total DESC)     AS Rank,       -- 1 2 2 4
       DENSE_RANK() OVER (ORDER BY Total DESC)     AS DenseRank   -- 1 2 2 3
FROM Orders
WHERE CustomerId = 1;
