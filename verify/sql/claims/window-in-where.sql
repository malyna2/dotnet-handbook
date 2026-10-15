-- A window function in WHERE is an error on both engines: windows are computed after WHERE.
SELECT CustomerId, Id, Total
FROM Orders
WHERE ROW_NUMBER() OVER (PARTITION BY CustomerId ORDER BY Total DESC, Id) <= 2;
