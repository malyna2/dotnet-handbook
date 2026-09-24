# QueryLab — Lab 37, reading execution plans

date        : 2026-09-24 12:35 UTC
server      : PostgreSQL 18.6 (Debian 18.6-1.pgdg13+2) on x86_64-pc-linux-gnu, compiled by gcc (Debian 14.2.0-19) 14.2.0, 64-bit
client      : .NET 10.0.12, Npgsql 10.0.3.0
machine     : 4 vCPU (Intel(R) Xeon(R) Processor @ 2.10GHz), 16 GB RAM
data        : 5,000,000 orders, ~18,012,788 order lines
cache state : warm — each rung runs once before it is measured
rungs       : solution implementation, 5 measured calls per rung

## Rung 1 — The order history page (solution)

Key account 2's latest 50 orders, newest first (created_at, then id), each with its lines in line-id order.

fixes       : none
wall time   : median 9.1 ms over 5 calls (min 7.4, max 84.9)
per call    : 1 statement(s), 1,182 rows returned, shared buffers 291 hit + 0 read, 1.7 ms server execution (pg_stat_statements)
statements  : 1 distinct SQL text(s) per call

```sql
SELECT o1.id, o1.created_at, o1.status, o1.total, o0.product_id, o0.quantity, o0.unit_price, o0.id
FROM (
    SELECT o.id, o.created_at, o.status, o.total
    FROM orders AS o
    WHERE o.customer_id = @customerId
    ORDER BY o.created_at DESC, o.id DESC
    LIMIT @p
) AS o1
LEFT JOIN order_lines AS o0 ON o1.id = o0.order_id
ORDER BY o1.created_at DESC, o1.id DESC, o0.id
```
parameters  : @customerId = 2 (bigint), @p = 50 (integer)
```
Incremental Sort  (cost=12.59..461.48 rows=2412 width=56) (actual time=0.209..1.067 rows=1182.00 loops=1)
  Sort Key: o.created_at DESC, o.id DESC, o0.id
  Presorted Key: o.created_at, o.id
  Full-sort Groups: 25  Sort Method: quicksort  Average Memory: 29kB  Peak Memory: 29kB
  Pre-sorted Groups: 3  Sort Method: quicksort  Average Memory: 26kB  Peak Memory: 26kB
  Buffers: shared hit=291
  ->  Nested Loop Left Join  (cost=4.06..362.88 rows=2412 width=56) (actual time=0.173..0.685 rows=1182.00 loops=1)
        Buffers: shared hit=291
        ->  Limit  (cost=3.63..165.01 rows=50 width=30) (actual time=0.167..0.261 rows=50.00 loops=1)
              Buffers: shared hit=81
              ->  Incremental Sort  (cost=3.63..68324.19 rows=21167 width=30) (actual time=0.166..0.256 rows=50.00 loops=1)
                    Sort Key: o.created_at DESC, o.id DESC
                    Presorted Key: o.created_at
                    Full-sort Groups: 2  Sort Method: quicksort  Average Memory: 27kB  Peak Memory: 27kB
                    Buffers: shared hit=81
                    ->  Index Scan Backward using ix_orders_created_at_customer_id on orders o  (cost=0.43..67371.68 rows=21167 width=30) (actual time=0.016..0.231 rows=51.00 loops=1)
                          Index Cond: (customer_id = '2'::bigint)
                          Index Searches: 1
                          Buffers: shared hit=81
        ->  Index Scan using ix_order_lines_order_id on order_lines o0  (cost=0.44..3.48 rows=48 width=34) (actual time=0.003..0.006 rows=23.64 loops=50)
              Index Cond: (order_id = o.id)
              Index Searches: 50
              Buffers: shared hit=210
Planning:
  Buffers: shared hit=9
Planning Time: 0.160 ms
Execution Time: 1.134 ms
```

## Rung 2 — The quarterly delivery report (solution)

Key account 2's shipped orders from the last 90 days in id order, each with its lines and its shipments (both in id order) — the file an account manager downloads.

fixes       : none
wall time   : median 308.4 ms over 5 calls (min 264.9, max 342.9)
per call    : 3 statement(s), 43,529 rows returned, shared buffers 21,711 hit + 0 read, 86.3 ms server execution (pg_stat_statements)
statements  : 3 distinct SQL text(s) per call

```sql
SELECT o.id, o.created_at, o.currency, o.customer_id, o.shipping_note, o.status, o.total
FROM orders AS o
WHERE o.customer_id = @customerId AND o.status = 'shipped' AND o.created_at >= @from AND o.created_at < @to
ORDER BY o.id
```
parameters  : @customerId = 2 (bigint), @from = 2026-06-03 00:00:00Z (timestamp with time zone), @to = 2026-09-01 00:00:00Z (timestamp with time zone)
```
Sort  (cost=7795.82..7799.78 rows=1587 width=85) (actual time=14.264..14.327 rows=1450.00 loops=1)
  Sort Key: id
  Sort Method: quicksort  Memory: 156kB
  Buffers: shared hit=3186
  ->  Index Scan using ix_orders_created_at_customer_id on orders o  (cost=0.43..7711.45 rows=1587 width=85) (actual time=0.052..13.947 rows=1450.00 loops=1)
        Index Cond: ((created_at >= '2026-06-03 00:00:00+00'::timestamp with time zone) AND (created_at < '2026-09-01 00:00:00+00'::timestamp with time zone) AND (customer_id = '2'::bigint))
        Filter: (status = 'shipped'::text)
        Rows Removed by Filter: 607
        Index Searches: 1
        Buffers: shared hit=3186
Planning:
  Buffers: shared hit=4
Planning Time: 0.119 ms
Execution Time: 14.467 ms
```

```sql
SELECT o0.id, o0.order_id, o0.product_id, o0.quantity, o0.unit_price, o.id
FROM orders AS o
INNER JOIN order_lines AS o0 ON o.id = o0.order_id
WHERE o.customer_id = @customerId AND o.status = 'shipped' AND o.created_at >= @from AND o.created_at < @to
ORDER BY o.id
```
parameters  : @customerId = 2 (bigint), @from = 2026-06-03 00:00:00Z (timestamp with time zone), @to = 2026-09-01 00:00:00Z (timestamp with time zone)
```
Gather Merge  (cost=12564.11..13215.68 rows=5717 width=42) (actual time=22.234..30.873 rows=36289.00 loops=1)
  Workers Planned: 1
  Workers Launched: 1
  Buffers: shared hit=9389
  ->  Sort  (cost=11564.10..11572.51 rows=3363 width=42) (actual time=20.290..21.133 rows=18144.50 loops=2)
        Sort Key: o0.order_id
        Sort Method: quicksort  Memory: 2107kB
        Buffers: shared hit=9389
        Worker 0:  Sort Method: quicksort  Memory: 1314kB
        ->  Nested Loop  (cost=0.87..11367.10 rows=3363 width=42) (actual time=0.104..17.156 rows=18144.50 loops=2)
              Buffers: shared hit=9381
              ->  Parallel Index Scan using ix_orders_created_at_customer_id on orders o  (cost=0.43..7702.52 rows=934 width=8) (actual time=0.079..8.969 rows=725.00 loops=2)
                    Index Cond: ((created_at >= '2026-06-03 00:00:00+00'::timestamp with time zone) AND (created_at < '2026-09-01 00:00:00+00'::timestamp with time zone) AND (customer_id = '2'::bigint))
                    Filter: (status = 'shipped'::text)
                    Rows Removed by Filter: 304
                    Index Searches: 1
                    Buffers: shared hit=3285
              ->  Index Scan using ix_order_lines_order_id on order_lines o0  (cost=0.44..3.44 rows=48 width=34) (actual time=0.006..0.008 rows=25.03 loops=1450)
                    Index Cond: (order_id = o.id)
                    Index Searches: 1450
                    Buffers: shared hit=6096
Planning:
  Buffers: shared hit=21
Planning Time: 0.323 ms
Execution Time: 32.594 ms
```

```sql
SELECT s.id, s.carrier, s.order_id, s.shipped_at, s.tracking_code, o.id
FROM orders AS o
INNER JOIN shipments AS s ON o.id = s.order_id
WHERE o.customer_id = @customerId AND o.status = 'shipped' AND o.created_at >= @from AND o.created_at < @to
ORDER BY o.id
```
parameters  : @customerId = 2 (bigint), @from = 2026-06-03 00:00:00Z (timestamp with time zone), @to = 2026-09-01 00:00:00Z (timestamp with time zone)
```
Gather Merge  (cost=11192.02..11381.67 rows=1664 width=70) (actual time=16.026..20.009 rows=5790.00 loops=1)
  Workers Planned: 1
  Workers Launched: 1
  Buffers: shared hit=9144
  ->  Sort  (cost=10192.01..10194.46 rows=979 width=70) (actual time=14.210..14.340 rows=2895.00 loops=2)
        Sort Key: s.order_id
        Sort Method: quicksort  Memory: 448kB
        Buffers: shared hit=9144
        Worker 0:  Sort Method: quicksort  Memory: 297kB
        ->  Nested Loop  (cost=0.86..10143.38 rows=979 width=70) (actual time=0.093..13.509 rows=2895.00 loops=2)
              Buffers: shared hit=9136
              ->  Parallel Index Scan using ix_orders_created_at_customer_id on orders o  (cost=0.43..7702.52 rows=934 width=8) (actual time=0.070..8.961 rows=725.00 loops=2)
                    Index Cond: ((created_at >= '2026-06-03 00:00:00+00'::timestamp with time zone) AND (created_at < '2026-09-01 00:00:00+00'::timestamp with time zone) AND (customer_id = '2'::bigint))
                    Filter: (status = 'shipped'::text)
                    Rows Removed by Filter: 304
                    Index Searches: 1
                    Buffers: shared hit=3281
              ->  Index Scan using ix_shipments_order_id on shipments s  (cost=0.43..2.59 rows=2 width=62) (actual time=0.005..0.005 rows=3.99 loops=1450)
                    Index Cond: (order_id = o.id)
                    Index Searches: 1450
                    Buffers: shared hit=5855
Planning:
  Buffers: shared hit=20
Planning Time: 0.245 ms
Execution Time: 20.268 ms
```

## Rung 3 — Signing in by email (solution)

Find the customer for the email address typed at sign-in. People type their address in any case; it is stored as they first entered it.

fixes       : CREATE UNIQUE INDEX lab_customers_email_lower ON customers (lower(email)) ; ANALYZE customers  (3.3 s to apply)
index size  : lab_customers_email_lower 49 MB
wall time   : median 1.9 ms over 5 calls (min 1.6, max 2.6)
per call    : 1 statement(s), 1 rows returned, shared buffers 4 hit + 0 read, 0.0 ms server execution (pg_stat_statements)
statements  : 1 distinct SQL text(s) per call

```sql
SELECT c.id, c.email, c.name
FROM customers AS c
WHERE lower(c.email) = @ToLower
LIMIT 2
```
parameters  : @ToLower = 'diego.larsen54321@corp.example' (text)
```
Limit  (cost=0.42..2.64 rows=1 width=51) (actual time=0.019..0.020 rows=1.00 loops=1)
  Buffers: shared hit=4
  ->  Index Scan using lab_customers_email_lower on customers c  (cost=0.42..2.64 rows=1 width=51) (actual time=0.018..0.019 rows=1.00 loops=1)
        Index Cond: (lower(email) = 'diego.larsen54321@corp.example'::text)
        Index Searches: 1
        Buffers: shared hit=4
Planning Time: 0.051 ms
Execution Time: 0.031 ms
```

## Rung 4 — The partner webhook (solution)

A delivery partner calls back with an order number, and the handler loads that order.

fixes       : none
wall time   : median 1.6 ms over 5 calls (min 1.5, max 2.3)
per call    : 1 statement(s), 1 rows returned, shared buffers 4 hit + 0 read, 0.0 ms server execution (pg_stat_statements)
statements  : 1 distinct SQL text(s) per call

```sql
SELECT o.id, o.customer_id, o.status, o.created_at, o.total
FROM orders AS o
WHERE o.id = @orderId
LIMIT 2
```
parameters  : @orderId = 424242 (bigint)
```
Limit  (cost=0.43..2.65 rows=1 width=38) (actual time=0.017..0.019 rows=1.00 loops=1)
  Buffers: shared hit=4
  ->  Index Scan using orders_pkey on orders o  (cost=0.43..2.65 rows=1 width=38) (actual time=0.016..0.017 rows=1.00 loops=1)
        Index Cond: (id = '424242'::bigint)
        Index Searches: 1
        Buffers: shared hit=4
Planning Time: 0.068 ms
Execution Time: 0.033 ms
```

## Rung 5 — Product sales figures (solution)

Order lines, units and revenue for product 50 across all orders — the figures on the product's admin page.

fixes       : CREATE INDEX lab_order_lines_product_id_covering ON order_lines (product_id) INCLUDE (quantity, unit_price)  (12.9 s to apply)
index size  : lab_order_lines_product_id_covering 697 MB
wall time   : median 5.8 ms over 5 calls (min 5.5, max 6.6)
per call    : 1 statement(s), 1 rows returned, shared buffers 98 hit + 0 read, 3.7 ms server execution (pg_stat_statements)
statements  : 1 distinct SQL text(s) per call

```sql
SELECT o.product_id, count(*)::int, COALESCE(sum(o.quantity::bigint), 0.0)::bigint, COALESCE(sum(o.quantity::numeric(10,2) * o.unit_price), 0.0)
FROM order_lines AS o
WHERE o.product_id = @productId
GROUP BY o.product_id
LIMIT 2
```
parameters  : @productId = 50 (bigint)
```
Limit  (cost=0.56..823.94 rows=1 width=52) (actual time=4.518..4.519 rows=1.00 loops=1)
  Buffers: shared hit=98
  ->  GroupAggregate  (cost=0.56..823.94 rows=1 width=52) (actual time=4.517..4.517 rows=1.00 loops=1)
        Buffers: shared hit=98
        ->  Index Only Scan using lab_order_lines_product_id_covering on order_lines o  (cost=0.56..477.14 rows=19816 width=18) (actual time=0.016..1.620 rows=18015.00 loops=1)
              Index Cond: (product_id = '50'::bigint)
              Heap Fetches: 0
              Index Searches: 1
              Buffers: shared hit=98
Planning Time: 0.076 ms
Execution Time: 4.542 ms
```

## Rung 6 — A customer's orders this year (solution)

The support screen: customer 33's orders from the last 365 days, newest first (created_at, then id).

fixes       : CREATE INDEX lab_orders_customer_id_created_at ON orders (customer_id, created_at)  (2.2 s to apply)
index size  : lab_orders_customer_id_created_at 150 MB
wall time   : median 1.5 ms over 5 calls (min 1.4, max 2.3)
per call    : 1 statement(s), 3 rows returned, shared buffers 6 hit + 0 read, 0.0 ms server execution (pg_stat_statements)
statements  : 1 distinct SQL text(s) per call

```sql
SELECT o.id, o.customer_id, o.status, o.created_at, o.total
FROM orders AS o
WHERE o.customer_id = @customerId AND o.created_at >= @since
ORDER BY o.created_at DESC, o.id DESC
```
parameters  : @customerId = 33 (bigint), @since = 2025-09-01 00:00:00Z (timestamp with time zone)
```
Incremental Sort  (cost=1.72..9.69 rows=7 width=38) (actual time=0.021..0.021 rows=3.00 loops=1)
  Sort Key: created_at DESC, id DESC
  Presorted Key: created_at
  Full-sort Groups: 1  Sort Method: quicksort  Average Memory: 25kB  Peak Memory: 25kB
  Buffers: shared hit=6
  ->  Index Scan Backward using lab_orders_customer_id_created_at on orders o  (cost=0.43..9.37 rows=7 width=38) (actual time=0.014..0.016 rows=3.00 loops=1)
        Index Cond: ((customer_id = '33'::bigint) AND (created_at >= '2025-09-01 00:00:00+00'::timestamp with time zone))
        Index Searches: 1
        Buffers: shared hit=6
Planning Time: 0.126 ms
Execution Time: 0.034 ms
```

## Rung 7 — The admin order list, page 4,001 (solution)

All orders, newest first (created_at, then id), 50 per page. The request carries the page number and the last row of the previous page.

fixes       : none
wall time   : median 1.7 ms over 5 calls (min 1.5, max 3.3)
per call    : 1 statement(s), 50 rows returned, shared buffers 5 hit + 0 read, 0.1 ms server execution (pg_stat_statements)
statements  : 1 distinct SQL text(s) per call

```sql
SELECT o.id, o.customer_id, o.status, o.created_at, o.total
FROM orders AS o
WHERE (o.created_at, o.id) < (@after_CreatedAt, @after_Id)
ORDER BY o.created_at DESC, o.id DESC
LIMIT @p
```
parameters  : @after_CreatedAt = 2026-07-19 03:50:41Z (timestamp with time zone), @after_Id = 4800001 (bigint), @p = 50 (integer)
```
Limit  (cost=0.48..4.59 rows=50 width=38) (actual time=0.045..0.059 rows=50.00 loops=1)
  Buffers: shared hit=5
  ->  Incremental Sort  (cost=0.48..394740.31 rows=4799239 width=38) (actual time=0.044..0.054 rows=50.00 loops=1)
        Sort Key: created_at DESC, id DESC
        Presorted Key: created_at
        Full-sort Groups: 2  Sort Method: quicksort  Average Memory: 27kB  Peak Memory: 27kB
        Buffers: shared hit=5
        ->  Index Scan Backward using ix_orders_created_at_customer_id on orders o  (cost=0.43..178774.56 rows=4799239 width=38) (actual time=0.026..0.037 rows=51.00 loops=1)
              Index Cond: (created_at <= '2026-07-19 03:50:41.811925+00'::timestamp with time zone)
              Filter: (ROW(created_at, id) < ROW('2026-07-19 03:50:41.811925+00'::timestamp with time zone, '4800001'::bigint))
              Rows Removed by Filter: 1
              Index Searches: 1
              Buffers: shared hit=5
Planning Time: 0.149 ms
Execution Time: 0.082 ms
```

## Rung 8 — Customer search (solution)

The support team's search box: customers whose email contains "tomasz.kowalski", ignoring case, in id order.

fixes       : CREATE EXTENSION IF NOT EXISTS pg_trgm ; CREATE INDEX lab_customers_email_trgm ON customers USING gin (email gin_trgm_ops)  (4.6 s to apply)
index size  : lab_customers_email_trgm 52 MB
wall time   : median 9.0 ms over 5 calls (min 8.5, max 10.3)
per call    : 1 statement(s), 249 rows returned, shared buffers 439 hit + 0 read, 7.1 ms server execution (pg_stat_statements)
statements  : 1 distinct SQL text(s) per call

```sql
SELECT c.id, c.email, c.name
FROM customers AS c
WHERE c.email ILIKE @pattern ESCAPE '\'
ORDER BY c.id
```
parameters  : @pattern = '%tomasz.kowalski%' (text)
```
Sort  (cost=179.95..180.20 rows=100 width=51) (actual time=6.136..6.148 rows=249.00 loops=1)
  Sort Key: id
  Sort Method: quicksort  Memory: 43kB
  Buffers: shared hit=439
  ->  Bitmap Heap Scan on customers c  (cost=66.24..176.63 rows=100 width=51) (actual time=5.577..6.085 rows=249.00 loops=1)
        Recheck Cond: (email ~~* '%tomasz.kowalski%'::text)
        Heap Blocks: exact=248
        Buffers: shared hit=439
        ->  Bitmap Index Scan on lab_customers_email_trgm  (cost=0.00..66.21 rows=100 width=0) (actual time=5.534..5.535 rows=249.00 loops=1)
              Index Cond: (email ~~* '%tomasz.kowalski%'::text)
              Index Searches: 1
              Buffers: shared hit=191
Planning:
  Buffers: shared hit=1
Planning Time: 0.160 ms
Execution Time: 6.178 ms
```

## Rung 9 — Recent sales of a product (solution)

The product page's "latest 20 sales" panel, on the service's real configuration: Npgsql automatic preparation is on (Max Auto Prepare=20). Traffic warms it up with 12 popular products; then someone opens the bestseller, product 1.

fixes       : CREATE INDEX lab_order_lines_product_id_id ON order_lines (product_id, id)  (11.0 s to apply)
index size  : lab_order_lines_product_id_id 542 MB
wall time   : median 1.5 ms over 5 calls (min 1.4, max 4.1)
per call    : 1 statement(s), 20 rows returned, shared buffers 7 hit + 0 read, 0.0 ms server execution (pg_stat_statements)
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
Limit  (cost=0.56..3.30 rows=20 width=26) (actual time=0.017..0.022 rows=20.00 loops=1)
  Buffers: shared hit=7
  ->  Index Scan Backward using lab_order_lines_product_id_id on order_lines o  (cost=0.56..196487.05 rows=1435162 width=26) (actual time=0.016..0.020 rows=20.00 loops=1)
        Index Cond: (product_id = '1'::bigint)
        Index Searches: 1
        Buffers: shared hit=7
Planning Time: 0.077 ms
Execution Time: 0.034 ms
```
