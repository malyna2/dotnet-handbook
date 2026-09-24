-- SQL Server rung 1: the same sign-in lookup, with the parameter typed two ways.
-- ADO.NET and Dapper send a .NET string as nvarchar unless told otherwise; this column is varchar.
USE shop;
SET NOCOUNT ON;
DECLARE @typed varchar(254) = (SELECT LOWER(email) FROM dbo.customers WHERE id = 54321);

PRINT '=== 1a: @email nvarchar(4000) — a .NET string sent with the default type';
SET STATISTICS IO ON; SET STATISTICS PROFILE ON;
EXEC sp_executesql N'SELECT id, email, name FROM dbo.customers WHERE email = @email',
                   N'@email nvarchar(4000)', @email = @typed;
SET STATISTICS PROFILE OFF; SET STATISTICS IO OFF;

PRINT '=== 1b: @email varchar(254) — the parameter typed like the column';
SET STATISTICS IO ON; SET STATISTICS PROFILE ON;
EXEC sp_executesql N'SELECT id, email, name FROM dbo.customers WHERE email = @email',
                   N'@email varchar(254)', @email = @typed;
SET STATISTICS PROFILE OFF; SET STATISTICS IO OFF;
