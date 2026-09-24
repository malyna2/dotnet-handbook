-- SQL Server rung 2: the key lookup, and the point where the optimizer stops using the index.
-- OPTION (RECOMPILE) on every query here, so each value gets the plan it deserves on its own
-- (rung 3 shows what happens when two values have to share one plan).
USE shop;
SET NOCOUNT ON;
DECLARE @q nvarchar(400) = N'SELECT id, created_at, status, total FROM dbo.orders WHERE customer_id = @c ORDER BY created_at DESC OPTION (RECOMPILE)';
DECLARE @forced nvarchar(400) = N'SELECT id, created_at, status, total FROM dbo.orders WITH (INDEX(ix_orders_customer_id)) WHERE customer_id = @c ORDER BY created_at DESC OPTION (RECOMPILE)';

PRINT '=== 2a: a typical customer: the index finds the rows, a key lookup fetches the other columns';
SET STATISTICS IO ON; SET STATISTICS PROFILE ON;
EXEC sp_executesql @q, N'@c bigint', @c = 54321;
SET STATISTICS PROFILE OFF; SET STATISTICS IO OFF;

PRINT '=== 2b: customer 1 (5% of all orders): same query, and the index is ignored';
SET STATISTICS IO ON; SET STATISTICS PROFILE ON;
EXEC sp_executesql @q, N'@c bigint', @c = 1;
SET STATISTICS PROFILE OFF; SET STATISTICS IO OFF;

PRINT '=== 2c: customer 1 with the index forced: what 100,000 key lookups cost';
SET STATISTICS IO ON; SET STATISTICS PROFILE ON;
EXEC sp_executesql @forced, N'@c bigint', @c = 1;
SET STATISTICS PROFILE OFF; SET STATISTICS IO OFF;

PRINT '=== 2d: the fix: CREATE INDEX lab_orders_customer_id_covering ON dbo.orders (customer_id) INCLUDE (created_at, status, total)';
CREATE INDEX lab_orders_customer_id_covering ON dbo.orders (customer_id) INCLUDE (created_at, status, total);
SET STATISTICS IO ON; SET STATISTICS PROFILE ON;
EXEC sp_executesql @q, N'@c bigint', @c = 54321;
EXEC sp_executesql @q, N'@c bigint', @c = 1;
SET STATISTICS PROFILE OFF; SET STATISTICS IO OFF;
DROP INDEX lab_orders_customer_id_covering ON dbo.orders;
