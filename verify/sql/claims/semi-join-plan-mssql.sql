-- The same four queries on SQL Server: IN and EXISTS a Left Semi Join, NOT EXISTS and NOT IN a Left Anti Semi Join (NOT IN with extra NULL handling).
SET SHOWPLAN_TEXT ON;
GO
SELECT Name FROM Customers c WHERE c.Id IN (SELECT o.CustomerId FROM Orders o);
SELECT Name FROM Customers c WHERE EXISTS (SELECT 1 FROM Orders o WHERE o.CustomerId = c.Id);
SELECT Name FROM Customers c WHERE NOT EXISTS (SELECT 1 FROM Orders o WHERE o.CustomerId = c.Id);
SELECT Name FROM Customers c WHERE c.Id NOT IN (SELECT o.CustomerId FROM Orders o);
GO
SET SHOWPLAN_TEXT OFF;
GO
