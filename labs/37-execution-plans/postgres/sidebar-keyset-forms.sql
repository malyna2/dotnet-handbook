-- Chapter 37, rung 7: the same page three ways — OFFSET, keyset written with OR, keyset written
-- as a row-value comparison; then the row-value form again with an index that matches the ORDER BY.
\set ON_ERROR_STOP on
SELECT created_at AS last_created_at, id AS last_id FROM orders
ORDER BY created_at DESC, id DESC OFFSET 199999 LIMIT 1 \gset
\echo '=== OFFSET 200000'
EXPLAIN (ANALYZE, BUFFERS) SELECT id, customer_id, status, created_at, total FROM orders
ORDER BY created_at DESC, id DESC OFFSET 200000 LIMIT 50;
\echo '=== keyset, OR form: created_at < x OR (created_at = x AND id < y)'
EXPLAIN (ANALYZE, BUFFERS) SELECT id, customer_id, status, created_at, total FROM orders
WHERE created_at < :'last_created_at' OR (created_at = :'last_created_at' AND id < :last_id)
ORDER BY created_at DESC, id DESC LIMIT 50;
\echo '=== keyset, row-value form: (created_at, id) < (x, y)'
EXPLAIN (ANALYZE, BUFFERS) SELECT id, customer_id, status, created_at, total FROM orders
WHERE (created_at, id) < (:'last_created_at'::timestamptz, :last_id)
ORDER BY created_at DESC, id DESC LIMIT 50;
\echo '=== keyset, row-value form, with an index on (created_at, id) (created and rolled back)'
BEGIN;
CREATE INDEX lab_orders_created_at_id ON orders (created_at, id);
EXPLAIN (ANALYZE, BUFFERS) SELECT id, customer_id, status, created_at, total FROM orders
WHERE (created_at, id) < (:'last_created_at'::timestamptz, :last_id)
ORDER BY created_at DESC, id DESC LIMIT 50;
ROLLBACK;
