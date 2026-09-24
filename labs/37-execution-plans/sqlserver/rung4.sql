-- SQL Server rung 4: page 4,001 of the admin order list, three ways.
-- ix_orders_created_at is non-unique, so SQL Server appends the clustering key (id) to its key:
-- the index is ordered by (created_at, id), exactly the page order.
USE shop;
SET NOCOUNT ON;
DECLARE @c datetime2(3), @id bigint;
SELECT @c = created_at, @id = id FROM dbo.orders
ORDER BY created_at DESC, id DESC OFFSET 199999 ROWS FETCH NEXT 1 ROWS ONLY;

PRINT '=== 4a: OFFSET 200000 ROWS';
SET STATISTICS IO ON; SET STATISTICS PROFILE ON;
EXEC sp_executesql N'SELECT id, customer_id, status, created_at, total FROM dbo.orders ORDER BY created_at DESC, id DESC OFFSET @skip ROWS FETCH NEXT 50 ROWS ONLY', N'@skip int', @skip = 200000;
SET STATISTICS PROFILE OFF; SET STATISTICS IO OFF;

PRINT '=== 4b: keyset, written as (created_at < @c) OR (created_at = @c AND id < @id)';
SET STATISTICS IO ON; SET STATISTICS PROFILE ON;
EXEC sp_executesql N'SELECT TOP (50) id, customer_id, status, created_at, total FROM dbo.orders WHERE created_at < @c OR (created_at = @c AND id < @id) ORDER BY created_at DESC, id DESC', N'@c datetime2(3), @id bigint', @c = @c, @id = @id;
SET STATISTICS PROFILE OFF; SET STATISTICS IO OFF;

PRINT '=== 4c: keyset with a redundant boundary: created_at <= @c AND (created_at < @c OR id < @id)';
SET STATISTICS IO ON; SET STATISTICS PROFILE ON;
EXEC sp_executesql N'SELECT TOP (50) id, customer_id, status, created_at, total FROM dbo.orders WHERE created_at <= @c AND (created_at < @c OR id < @id) ORDER BY created_at DESC, id DESC', N'@c datetime2(3), @id bigint', @c = @c, @id = @id;
SET STATISTICS PROFILE OFF; SET STATISTICS IO OFF;
