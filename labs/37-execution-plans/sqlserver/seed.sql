-- Lab 37, SQL Server track — schema and deterministic seed (run through ../sqlserver.sh seed).
-- 200 000 customers, 2 000 000 orders. Same skew as the PostgreSQL track: customer 1 places 5%
-- of all orders. Emails are varchar with the server's default collation, as many real schemas are.
SET NOCOUNT ON;
IF DB_ID('shop') IS NULL CREATE DATABASE shop;
GO
USE shop;
GO
DROP TABLE IF EXISTS dbo.orders;
DROP TABLE IF EXISTS dbo.customers;
GO
CREATE TABLE dbo.customers (
    id          bigint        NOT NULL CONSTRAINT pk_customers PRIMARY KEY CLUSTERED,
    email       varchar(254)  NOT NULL,
    name        nvarchar(200) NOT NULL,
    created_at  datetime2(3)  NOT NULL
);
CREATE TABLE dbo.orders (
    id           bigint        NOT NULL CONSTRAINT pk_orders PRIMARY KEY CLUSTERED,
    customer_id  bigint        NOT NULL,
    status       varchar(16)   NOT NULL,
    created_at   datetime2(3)  NOT NULL,
    total        decimal(12,2) NOT NULL,
    shipping_note nvarchar(200) NULL
);
GO
-- A deterministic "random" number in [0, 1) from a key and a salt.
CREATE OR ALTER FUNCTION dbo.r(@k bigint, @salt int) RETURNS float
WITH SCHEMABINDING, RETURNS NULL ON NULL INPUT
AS BEGIN
    RETURN (ABS(CAST(HASHBYTES('SHA2_256', CONCAT(@k, ':', @salt)) AS bigint)) % 1000000000) / 1000000000.0;
END;
GO
DECLARE @customers bigint = 200000, @orders bigint = 2000000;

INSERT INTO dbo.customers WITH (TABLOCK) (id, email, name, created_at)
SELECT s.value,
       CONCAT(f.v, '.', l.v, s.value, '@', CASE WHEN dbo.r(s.value, 3) < 0.5 THEN 'Example.com' ELSE 'Mail.test' END),
       CONCAT(f.v, ' ', l.v),
       DATEADD(day, CAST(dbo.r(s.value, 6) * 1826 AS int), '2021-09-01')
FROM GENERATE_SERIES(CAST(1 AS bigint), @customers) AS s
CROSS APPLY (SELECT CHOOSE(1 + CAST(dbo.r(s.value, 1) * 10 AS int),
    'Anna','Olena','Maria','Tomasz','Piotr','Lukas','Emma','Jonas','Sofia','Marco') AS v) AS f
CROSS APPLY (SELECT CHOOSE(1 + CAST(dbo.r(s.value, 2) * 10 AS int),
    'Kowalski','Nowak','Shevchenko','Mueller','Schmidt','Rossi','Garcia','Smith','Novak','Larsen') AS v) AS l;

INSERT INTO dbo.orders WITH (TABLOCK) (id, customer_id, status, created_at, total, shipping_note)
SELECT s.value,
       CASE WHEN dbo.r(s.value, 11) < 0.05 THEN 1
            ELSE 2 + CAST(dbo.r(s.value, 12) * (@customers - 1) AS bigint) END,
       CASE WHEN dbo.r(s.value, 13) < 0.02 THEN 'open' WHEN dbo.r(s.value, 13) < 0.04 THEN 'paid'
            WHEN dbo.r(s.value, 13) < 0.95 THEN 'shipped' ELSE 'cancelled' END,
       DATEADD(second, CAST(((s.value - 1 + dbo.r(s.value, 14)) / @orders) * 1096 * 86400 AS int), '2023-09-01'),
       CAST(5 + dbo.r(s.value, 15) * 995 AS decimal(12,2)),
       CASE WHEN dbo.r(s.value, 16) < 0.1 THEN N'Leave with the neighbour if nobody answers' END
FROM GENERATE_SERIES(CAST(1 AS bigint), @orders) AS s
OPTION (MAXDOP 4);
GO
-- The baseline indexes. The foreign-key column is indexed on its own — no INCLUDE.
CREATE UNIQUE INDEX uq_customers_email ON dbo.customers (email);
CREATE INDEX ix_orders_customer_id ON dbo.orders (customer_id);
CREATE INDEX ix_orders_created_at ON dbo.orders (created_at);
UPDATE STATISTICS dbo.customers WITH FULLSCAN;
UPDATE STATISTICS dbo.orders WITH FULLSCAN;
GO
SELECT 'customers' AS [table], COUNT_BIG(*) AS [rows] FROM dbo.customers
UNION ALL SELECT 'orders', COUNT_BIG(*) FROM dbo.orders
UNION ALL SELECT 'orders of customer 1', COUNT_BIG(*) FROM dbo.orders WHERE customer_id = 1;
SELECT DATABASEPROPERTYEX('shop', 'Collation') AS collation, compatibility_level
FROM sys.databases WHERE name = 'shop';
GO
