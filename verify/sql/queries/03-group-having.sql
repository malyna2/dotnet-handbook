SELECT CustomerId, COUNT(*) AS PaidOrders, SUM(Total) AS Paid
FROM Orders
WHERE Status = 'Paid'       -- filters rows, before grouping
GROUP BY CustomerId
HAVING COUNT(*) > 1;        -- filters groups, after aggregating
