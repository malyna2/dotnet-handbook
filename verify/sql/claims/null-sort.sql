-- Dialect difference: where NULL sorts. SQL Server puts Cleo's NULL city first, PostgreSQL last.
SELECT Name, City FROM Customers ORDER BY City, Name;
