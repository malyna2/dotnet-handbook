-- SQL Server expands a CTE at every reference: referenced twice, Orders is read twice.
SET SHOWPLAN_TEXT ON;
GO
WITH Paid AS (SELECT CustomerId, Total FROM Orders WHERE Status = 'Paid')
SELECT * FROM Paid p1 JOIN Paid p2 ON p2.CustomerId = p1.CustomerId;
GO
SET SHOWPLAN_TEXT OFF;
GO
