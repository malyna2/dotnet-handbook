# QueryLab — Lab 37, reading execution plans

date        : 2026-09-24 12:35 UTC
server      : PostgreSQL 18.6 (Debian 18.6-1.pgdg13+2) on x86_64-pc-linux-gnu, compiled by gcc (Debian 14.2.0-19) 14.2.0, 64-bit
client      : .NET 10.0.12, Npgsql 10.0.3.0
machine     : 4 vCPU (Intel(R) Xeon(R) Processor @ 2.10GHz), 16 GB RAM
data        : 5,000,000 orders, ~18,012,788 order lines
cache state : warm — each rung runs once before it is measured
rungs       : starter implementation, 5 measured calls per rung

## Rung 1 — The order history page (starter)

Key account 2's latest 50 orders, newest first (created_at, then id), each with its lines in line-id order.

fixes       : none
wall time   : median 78.0 ms over 5 calls (min 59.3, max 129.5)
per call    : 51 statement(s), 1,232 rows returned, shared buffers 291 hit + 0 read, 2.0 ms server execution (pg_stat_statements)
statements  : 2 distinct SQL text(s) per call

```sql
SELECT o.id, o.created_at, o.currency, o.customer_id, o.shipping_note, o.status, o.total
FROM orders AS o
WHERE o.customer_id = @customerId
ORDER BY o.created_at DESC, o.id DESC
LIMIT @p
```
parameters  : @customerId = 2 (bigint), @p = 50 (integer)
```
Limit  (cost=3.63..165.01 rows=50 width=85) (actual time=0.208..0.326 rows=50.00 loops=1)
  Buffers: shared hit=81
  ->  Incremental Sort  (cost=3.63..68324.19 rows=21167 width=85) (actual time=0.207..0.318 rows=50.00 loops=1)
        Sort Key: created_at DESC, id DESC
        Presorted Key: created_at
        Full-sort Groups: 2  Sort Method: quicksort  Average Memory: 27kB  Peak Memory: 27kB
        Buffers: shared hit=81
        ->  Index Scan Backward using ix_orders_created_at_customer_id on orders o  (cost=0.43..67371.68 rows=21167 width=85) (actual time=0.028..0.299 rows=51.00 loops=1)
              Index Cond: (customer_id = '2'::bigint)
              Index Searches: 1
              Buffers: shared hit=81
Planning Time: 0.069 ms
Execution Time: 0.344 ms
```

```sql
SELECT o.product_id, o.quantity, o.unit_price
FROM order_lines AS o
WHERE o.order_id = @orderId
ORDER BY o.id
```
parameters  : @orderId = 4999826 (bigint)
```
Sort  (cost=4.82..4.94 rows=48 width=26) (actual time=0.024..0.025 rows=17.00 loops=1)
  Sort Key: id
  Sort Method: quicksort  Memory: 25kB
  Buffers: shared hit=4
  ->  Index Scan using ix_order_lines_order_id on order_lines o  (cost=0.44..3.48 rows=48 width=26) (actual time=0.015..0.018 rows=17.00 loops=1)
        Index Cond: (order_id = '4999826'::bigint)
        Index Searches: 1
        Buffers: shared hit=4
Planning Time: 0.054 ms
Execution Time: 0.041 ms
```

## Rung 2 — The quarterly delivery report (starter)

Key account 2's shipped orders from the last 90 days in id order, each with its lines and its shipments (both in id order) — the file an account manager downloads.

fixes       : none
wall time   : median 616.7 ms over 5 calls (min 586.8, max 809.8)
per call    : 1 statement(s), 144,843 rows returned, shared buffers 33,435 hit + 0 read, 425.4 ms server execution (pg_stat_statements)
statements  : 1 distinct SQL text(s) per call
EF warning  : RelationalEventId.MultipleCollectionIncludeWarning[20504] — Compiling a query which loads related collections for more than one collection navigation, either via 'Include' or through projection, but no 'QuerySplittingBehavior' has been configured. By default, Entity Framework will use 'QuerySplittingBehavior.SingleQuery', which can potentially result in slow query performance. See https://go.microsoft.com/fwlink/?linkid=2134277 for more information. To identify the query that's triggering this warning call 'ConfigureWarnings(w => w.Throw(RelationalEventId.MultipleCollectionIncludeWarning))'.

```sql
SELECT o.id, o.created_at, o.currency, o.customer_id, o.shipping_note, o.status, o.total, o0.id, o0.order_id, o0.product_id, o0.quantity, o0.unit_price, s.id, s.carrier, s.order_id, s.shipped_at, s.tracking_code
FROM orders AS o
LEFT JOIN order_lines AS o0 ON o.id = o0.order_id
LEFT JOIN shipments AS s ON o.id = s.order_id
WHERE o.customer_id = @customerId AND o.status = 'shipped' AND o.created_at >= @from AND o.created_at < @to
ORDER BY o.id, o0.id
```
parameters  : @customerId = 2 (bigint), @from = 2026-06-03 00:00:00Z (timestamp with time zone), @to = 2026-09-01 00:00:00Z (timestamp with time zone)
```
Gather Merge  (cost=15192.28..15875.42 rows=5994 width=181) (actual time=116.055..158.740 rows=144843.00 loops=1)
  Workers Planned: 1
  Workers Launched: 1
  Buffers: shared hit=33435, temp read=3262 written=3269
  I/O Timings: temp read=4.659 write=25.010
  ->  Sort  (cost=14192.27..14201.08 rows=3526 width=181) (actual time=112.548..124.757 rows=72421.50 loops=2)
        Sort Key: o.id, o0.id
        Sort Method: external merge  Disk: 16704kB
        Buffers: shared hit=33435, temp read=3262 written=3269
        I/O Timings: temp read=4.659 write=25.010
        Worker 0:  Sort Method: external merge  Disk: 9392kB
        ->  Nested Loop Left Join  (cost=1.30..13984.52 rows=3526 width=181) (actual time=0.137..39.523 rows=72421.50 loops=2)
              Buffers: shared hit=33427
              ->  Nested Loop Left Join  (cost=0.86..10143.38 rows=979 width=147) (actual time=0.110..12.008 rows=2895.00 loops=2)
                    Buffers: shared hit=9118
                    ->  Parallel Index Scan using ix_orders_created_at_customer_id on orders o  (cost=0.43..7702.52 rows=934 width=85) (actual time=0.089..7.948 rows=725.00 loops=2)
                          Index Cond: ((created_at >= '2026-06-03 00:00:00+00'::timestamp with time zone) AND (created_at < '2026-09-01 00:00:00+00'::timestamp with time zone) AND (customer_id = '2'::bigint))
                          Filter: (status = 'shipped'::text)
                          Rows Removed by Filter: 304
                          Index Searches: 1
                          Buffers: shared hit=3263
                    ->  Index Scan using ix_shipments_order_id on shipments s  (cost=0.43..2.59 rows=2 width=62) (actual time=0.004..0.005 rows=3.99 loops=1450)
                          Index Cond: (order_id = o.id)
                          Index Searches: 1450
                          Buffers: shared hit=5855
              ->  Index Scan using ix_order_lines_order_id on order_lines o0  (cost=0.44..3.44 rows=48 width=34) (actual time=0.003..0.006 rows=25.02 loops=5790)
                    Index Cond: (order_id = o.id)
                    Index Searches: 5790
                    Buffers: shared hit=24309
Planning:
  Buffers: shared hit=37
Planning Time: 0.402 ms
Execution Time: 166.906 ms
```

## Rung 3 — Signing in by email (starter)

Find the customer for the email address typed at sign-in. People type their address in any case; it is stored as they first entered it.

fixes       : none
wall time   : median 318.0 ms over 5 calls (min 308.1, max 385.9)
per call    : 1 statement(s), 1 rows returned, shared buffers 13,659 hit + 0 read, 327.0 ms server execution (pg_stat_statements)
statements  : 1 distinct SQL text(s) per call

```sql
SELECT c.id, c.email, c.name
FROM customers AS c
WHERE lower(c.email) = @ToLower
LIMIT 2
```
parameters  : @ToLower = 'diego.larsen54321@corp.example' (text)
```
Limit  (cost=0.00..11.46 rows=2 width=51) (actual time=16.364..303.337 rows=1.00 loops=1)
  Buffers: shared hit=13659
  ->  Seq Scan on customers c  (cost=0.00..28659.00 rows=5000 width=51) (actual time=16.361..303.331 rows=1.00 loops=1)
        Filter: (lower(email) = 'diego.larsen54321@corp.example'::text)
        Rows Removed by Filter: 999999
        Buffers: shared hit=13659
Planning Time: 0.090 ms
Execution Time: 303.369 ms
```

## Rung 4 — The partner webhook (starter)

A delivery partner calls back with an order number, and the handler loads that order.

fixes       : none
wall time   : median 488.2 ms over 5 calls (min 458.7, max 547.6)
per call    : 1 statement(s), 1 rows returned, shared buffers 6,741 hit + 42,702 read, 501.6 ms server execution (pg_stat_statements)
statements  : 1 distinct SQL text(s) per call

```sql
SELECT o.id, o.customer_id, o.status, o.created_at, o.total
FROM orders AS o
WHERE o.id::numeric = @payload_OrderNumber
LIMIT 2
```
parameters  : @payload_OrderNumber = 424242 (numeric)
```
Limit  (cost=0.00..9.96 rows=2 width=38) (actual time=47.032..489.601 rows=1.00 loops=1)
  Buffers: shared hit=7893 read=41550
  I/O Timings: shared read=1.969
  ->  Seq Scan on orders o  (cost=0.00..124443.00 rows=25000 width=38) (actual time=47.013..489.580 rows=1.00 loops=1)
        Filter: ((id)::numeric = '424242'::numeric)
        Rows Removed by Filter: 4999999
        Buffers: shared hit=7893 read=41550
        I/O Timings: shared read=1.969
Planning Time: 0.088 ms
Execution Time: 489.631 ms
```

## Rung 5 — Product sales figures (starter)

Order lines, units and revenue for product 50 across all orders — the figures on the product's admin page.

fixes       : none
wall time   : median 32.5 ms over 5 calls (min 26.7, max 36.7)
per call    : 1 statement(s), 1 rows returned, shared buffers 17,011 hit + 0 read, 28.7 ms server execution (pg_stat_statements)
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
Limit  (cost=172.70..20561.24 rows=1 width=52) (actual time=24.294..24.297 rows=1.00 loops=1)
  Buffers: shared hit=17011
  ->  GroupAggregate  (cost=172.70..20561.24 rows=1 width=52) (actual time=24.293..24.294 rows=1.00 loops=1)
        Buffers: shared hit=17011
        ->  Bitmap Heap Scan on order_lines o  (cost=172.70..20214.47 rows=19814 width=18) (actual time=3.803..20.757 rows=18015.00 loops=1)
              Recheck Cond: (product_id = '50'::bigint)
              Heap Blocks: exact=16993
              Buffers: shared hit=17011
              ->  Bitmap Index Scan on ix_order_lines_product_id  (cost=0.00..167.74 rows=19814 width=0) (actual time=1.412..1.413 rows=18015.00 loops=1)
                    Index Cond: (product_id = '50'::bigint)
                    Index Searches: 1
                    Buffers: shared hit=18
Planning Time: 0.105 ms
Execution Time: 24.340 ms
```

## Rung 6 — A customer's orders this year (starter)

The support screen: customer 33's orders from the last 365 days, newest first (created_at, then id).

fixes       : none
wall time   : median 37.8 ms over 5 calls (min 30.6, max 40.3)
per call    : 1 statement(s), 3 rows returned, shared buffers 6,386 hit + 0 read, 33.3 ms server execution (pg_stat_statements)
statements  : 1 distinct SQL text(s) per call

```sql
SELECT o.id, o.customer_id, o.status, o.created_at, o.total
FROM orders AS o
WHERE o.customer_id = @customerId AND o.created_at >= @since
ORDER BY o.created_at DESC, o.id DESC
```
parameters  : @customerId = 33 (bigint), @since = 2025-09-01 00:00:00Z (timestamp with time zone)
```
Incremental Sort  (cost=3326.33..23281.97 rows=7 width=38) (actual time=26.700..26.702 rows=3.00 loops=1)
  Sort Key: created_at DESC, id DESC
  Presorted Key: created_at
  Full-sort Groups: 1  Sort Method: quicksort  Average Memory: 25kB  Peak Memory: 25kB
  Buffers: shared hit=6386
  ->  Index Scan Backward using ix_orders_created_at_customer_id on orders o  (cost=0.43..23281.65 rows=7 width=38) (actual time=3.844..26.684 rows=3.00 loops=1)
        Index Cond: ((created_at >= '2025-09-01 00:00:00+00'::timestamp with time zone) AND (customer_id = '33'::bigint))
        Index Searches: 1
        Buffers: shared hit=6386
Planning Time: 0.133 ms
Execution Time: 26.734 ms
```

## Rung 7 — The admin order list, page 4,001 (starter)

All orders, newest first (created_at, then id), 50 per page. The request carries the page number and the last row of the previous page.

fixes       : none
wall time   : median 44.4 ms over 5 calls (min 43.3, max 50.9)
per call    : 1 statement(s), 50 rows returned, shared buffers 2,791 hit + 0 read, 43.9 ms server execution (pg_stat_statements)
statements  : 1 distinct SQL text(s) per call

```sql
SELECT o.id, o.customer_id, o.status, o.created_at, o.total
FROM orders AS o
ORDER BY o.created_at DESC, o.id DESC
LIMIT @p1 OFFSET @p
```
parameters  : @p1 = 50 (integer), @p = 200000 (integer)
```
Limit  (cost=14910.75..14914.48 rows=50 width=38) (actual time=70.231..70.243 rows=50.00 loops=1)
  Buffers: shared hit=2791
  ->  Incremental Sort  (cost=0.47..372757.42 rows=5000000 width=38) (actual time=0.047..62.794 rows=200050.00 loops=1)
        Sort Key: created_at DESC, id DESC
        Presorted Key: created_at
        Full-sort Groups: 6252  Sort Method: quicksort  Average Memory: 27kB  Peak Memory: 27kB
        Buffers: shared hit=2791
        ->  Index Scan Backward using ix_orders_created_at_customer_id on orders o  (cost=0.43..147757.42 rows=5000000 width=38) (actual time=0.019..28.279 rows=200051.00 loops=1)
              Index Searches: 1
              Buffers: shared hit=2791
Planning Time: 0.145 ms
Execution Time: 70.274 ms
```

## Rung 8 — Customer search (starter)

The support team's search box: customers whose email contains "tomasz.kowalski", ignoring case, in id order.

fixes       : none
wall time   : median 202.7 ms over 5 calls (min 199.7, max 231.6)
per call    : 1 statement(s), 249 rows returned, shared buffers 13,735 hit + 0 read, 205.3 ms server execution (pg_stat_statements)
statements  : 1 distinct SQL text(s) per call

```sql
SELECT c.id, c.email, c.name
FROM customers AS c
WHERE c.email ILIKE @p ESCAPE ''
ORDER BY c.id
```
parameters  : @p = '%tomasz.kowalski%' (text)
```
Gather Merge  (cost=19868.49..19880.14 rows=100 width=51) (actual time=210.800..214.100 rows=249.00 loops=1)
  Workers Planned: 2
  Workers Launched: 2
  Buffers: shared hit=13735
  ->  Sort  (cost=18868.47..18868.57 rows=42 width=51) (actual time=207.975..207.981 rows=83.00 loops=3)
        Sort Key: id
        Sort Method: quicksort  Memory: 31kB
        Buffers: shared hit=13735
        Worker 0:  Sort Method: quicksort  Memory: 32kB
        Worker 1:  Sort Method: quicksort  Memory: 30kB
        ->  Parallel Seq Scan on customers c  (cost=0.00..18867.33 rows=42 width=51) (actual time=2.194..207.805 rows=83.00 loops=3)
              Filter: (email ~~* '%tomasz.kowalski%'::text)
              Rows Removed by Filter: 333250
              Buffers: shared hit=13659
Planning Time: 0.180 ms
Execution Time: 214.141 ms
```

## Rung 9 — Recent sales of a product (starter)

The product page's "latest 20 sales" panel, on the service's real configuration: Npgsql automatic preparation is on (Max Auto Prepare=20). Traffic warms it up with 12 popular products; then someone opens the bestseller, product 1.

fixes       : none
wall time   : median 1011.8 ms over 5 calls (min 945.7, max 1090.7)
per call    : 1 statement(s), 20 rows returned, shared buffers 2,307 hit + 149,024 read, 1014.2 ms server execution (pg_stat_statements)
statements  : 1 distinct SQL text(s) per call

```sql
SELECT o.id, o.order_id, o.quantity, o.unit_price
FROM order_lines AS o
WHERE o.product_id = $1
ORDER BY o.id DESC
LIMIT $2
```
parameters  : PREPARE lab_replay; productId, p = 1, 20 after 6 warm-up executions — plans so far: 3 generic, 5 custom
```
Limit  (cost=539.39..539.51 rows=46 width=26) (actual time=1070.082..1070.087 rows=20.00 loops=1)
  Buffers: shared read=151331
  I/O Timings: shared read=488.228
  ->  Sort  (cost=539.39..540.55 rows=463 width=26) (actual time=1070.079..1070.081 rows=20.00 loops=1)
        Sort Key: id DESC
        Sort Method: top-N heapsort  Memory: 27kB
        Buffers: shared read=151331
        I/O Timings: shared read=488.228
        ->  Index Scan using ix_order_lines_product_id on order_lines o  (cost=0.44..518.89 rows=463 width=26) (actual time=0.055..845.318 rows=1441651.00 loops=1)
              Index Cond: (product_id = $1)
              Index Searches: 1
              Buffers: shared read=151331
              I/O Timings: shared read=488.228
Planning Time: 0.030 ms
Execution Time: 1070.263 ms
```
