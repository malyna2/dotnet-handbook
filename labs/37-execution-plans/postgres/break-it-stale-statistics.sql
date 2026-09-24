-- Chapter 37, Break it: a bulk load the planner hasn't heard about yet.
-- Everything happens inside one transaction and is rolled back: the database is unchanged afterwards.
\set ON_ERROR_STOP on
BEGIN;
-- A plain index on customer_id gives the planner a second way to answer the query below.
CREATE INDEX lab_orders_customer_id ON orders (customer_id);
ANALYZE orders;
\echo '=== what the planner knows about orders.customer_id'
-- For a value outside the most-common-values list, the planner shares the remaining rows evenly
-- among the remaining distinct values: (1 - sum of MCV frequencies) / (n_distinct - MCV count).
SELECT s.n_distinct, cardinality(s.most_common_freqs) AS mcv_count,
       round(m.total::numeric, 4) AS mcv_total_frequency, c.reltuples::bigint AS reltuples,
       round(((1 - m.total) / (s.n_distinct - cardinality(s.most_common_freqs)) * c.reltuples)::numeric, 1)
         AS rows_for_an_unseen_value
FROM pg_stats AS s
CROSS JOIN LATERAL (SELECT sum(f) AS total FROM unnest(s.most_common_freqs) AS f) AS m
JOIN pg_class AS c ON c.relname = s.tablename
WHERE s.tablename = 'orders' AND s.attname = 'customer_id';
-- Customer 900001, a consumer account with three orders, becomes a marketplace seller and
-- bulk-loads 300,000 orders from the last twelve hours.
INSERT INTO orders (customer_id, status, created_at, total, currency)
SELECT 900001, 'paid', timestamptz '2026-08-31 12:00' + g * interval '100 milliseconds', 10, 'EUR'
FROM generate_series(1, 300000) AS g;
\echo '=== 300,000 new orders, statistics not refreshed yet'
EXPLAIN (ANALYZE, BUFFERS) SELECT id, created_at, total FROM orders
WHERE customer_id = 900001 ORDER BY created_at DESC LIMIT 50;
ANALYZE orders;
\echo '=== after ANALYZE orders'
EXPLAIN (ANALYZE, BUFFERS) SELECT id, created_at, total FROM orders
WHERE customer_id = 900001 ORDER BY created_at DESC LIMIT 50;
ROLLBACK;
-- ROLLBACK does not give the space back: the 300,000 rows stay in the heap and the indexes as dead
-- tuples, and every later scan of recent orders would wade through them. VACUUM removes them and
-- ANALYZE puts the statistics back to describing the seeded data. The index pages the inserts split
-- stay split, so REINDEX rebuilds them: the other rungs' buffer counts depend on that layout.
VACUUM (ANALYZE) orders;
REINDEX TABLE orders;
