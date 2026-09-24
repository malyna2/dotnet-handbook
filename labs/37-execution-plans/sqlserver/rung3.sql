-- SQL Server rung 3: parameter sniffing. One stored procedure, two customers, one cached plan.
USE shop;
SET NOCOUNT ON;
GO
CREATE OR ALTER PROCEDURE dbo.customer_orders @customer_id bigint AS
    SELECT id, created_at, status, total FROM dbo.orders WHERE customer_id = @customer_id ORDER BY created_at DESC;
GO
PRINT '=== 3a: cold cache; a typical customer calls first, then customer 1';
ALTER DATABASE SCOPED CONFIGURATION CLEAR PROCEDURE_CACHE;   -- this database only
EXEC dbo.customer_orders @customer_id = 54321;
SET STATISTICS IO ON; SET STATISTICS PROFILE ON;
EXEC dbo.customer_orders @customer_id = 1;
SET STATISTICS PROFILE OFF; SET STATISTICS IO OFF;
GO
PRINT '=== 3b: cold cache; customer 1 calls first, then a typical customer';
ALTER DATABASE SCOPED CONFIGURATION CLEAR PROCEDURE_CACHE;
EXEC dbo.customer_orders @customer_id = 1;
SET STATISTICS IO ON; SET STATISTICS PROFILE ON;
EXEC dbo.customer_orders @customer_id = 54321;
SET STATISTICS PROFILE OFF; SET STATISTICS IO OFF;
GO
PRINT '=== 3c: what the plan cache holds for the procedure''s statement';
SELECT qs.execution_count AS execs, qs.total_logical_reads AS logical_reads,
       CASE WHEN CAST(qp.query_plan AS nvarchar(max)) LIKE '%Dispatcher%' THEN 'yes' ELSE 'no' END AS psp_dispatcher,
       LEFT(REPLACE(REPLACE(st.text, CHAR(13), ' '), CHAR(10), ' '), 120) AS statement
FROM sys.dm_exec_query_stats qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) qp
WHERE st.text LIKE '%FROM dbo.orders%WHERE customer_id = @customer_id%'
  AND st.text NOT LIKE '%dm_exec_query_stats%';
GO
PRINT '=== 3d: the fix that removes the choice: a covering index is right for both customers';
CREATE INDEX lab_orders_customer_id_covering ON dbo.orders (customer_id) INCLUDE (created_at, status, total);
ALTER DATABASE SCOPED CONFIGURATION CLEAR PROCEDURE_CACHE;
EXEC dbo.customer_orders @customer_id = 54321;
SET STATISTICS IO ON; SET STATISTICS PROFILE ON;
EXEC dbo.customer_orders @customer_id = 1;
SET STATISTICS PROFILE OFF; SET STATISTICS IO OFF;
DROP INDEX lab_orders_customer_id_covering ON dbo.orders;
GO
