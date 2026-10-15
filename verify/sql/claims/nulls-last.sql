-- PostgreSQL accepts NULLS FIRST / NULLS LAST; SQL Server 2022 rejects it.
SELECT Name, City FROM Customers ORDER BY City NULLS LAST, Name;
