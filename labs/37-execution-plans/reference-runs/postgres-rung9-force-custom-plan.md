# QueryLab — Lab 37, reading execution plans

date        : 2026-09-24 12:36 UTC
server      : PostgreSQL 18.6 (Debian 18.6-1.pgdg13+2) on x86_64-pc-linux-gnu, compiled by gcc (Debian 14.2.0-19) 14.2.0, 64-bit
client      : .NET 10.0.12, Npgsql 10.0.3.0
machine     : 4 vCPU (Intel(R) Xeon(R) Processor @ 2.10GHz), 16 GB RAM
data        : 5,000,000 orders, ~18,014,590 order lines
cache state : warm — each rung runs once before it is measured
rungs       : starter implementation, 5 measured calls per rung

## Rung 9 — Recent sales of a product (starter)

The product page's "latest 20 sales" panel, on the service's real configuration: Npgsql automatic preparation is on (Max Auto Prepare=20). Traffic warms it up with 12 popular products; then someone opens the bestseller, product 1.

fixes       : none
wall time   : median 1.9 ms over 5 calls (min 1.7, max 5.2)
per call    : 1 statement(s), 20 rows returned, shared buffers 7 hit + 0 read, 0.1 ms server execution (pg_stat_statements)
statements  : 1 distinct SQL text(s) per call

```sql
SELECT o.id, o.order_id, o.quantity, o.unit_price
FROM order_lines AS o
WHERE o.product_id = $1
ORDER BY o.id DESC
LIMIT $2
```
parameters  : PREPARE lab_replay; productId, p = 1, 20 after 6 warm-up executions — plans so far: 0 generic, 8 custom
```
Limit  (cost=0.44..7.68 rows=20 width=26) (actual time=0.014..0.035 rows=20.00 loops=1)
  Buffers: shared hit=7
  ->  Index Scan Backward using order_lines_pkey on order_lines o  (cost=0.44..519864.56 rows=1435162 width=26) (actual time=0.013..0.033 rows=20.00 loops=1)
        Filter: (product_id = '1'::bigint)
        Rows Removed by Filter: 225
        Index Searches: 1
        Buffers: shared hit=7
Planning Time: 0.114 ms
Execution Time: 0.047 ms
```
