-- Chapter 37, Break it: the visibility map behind rung 5's index-only scan.
-- The DELETE is rolled back, and the script ends with VACUUM and DROP INDEX: the data, its physical
-- order and the indexes are as seeded afterwards.
\set ON_ERROR_STOP on
CREATE INDEX lab_order_lines_product_id_covering ON order_lines (product_id) INCLUDE (quantity, unit_price);
\echo '=== index-only scan, visibility map up to date'
EXPLAIN (ANALYZE, BUFFERS) SELECT count(*), sum(quantity), sum(quantity * unit_price)
FROM order_lines WHERE product_id = 50;
-- Delete every line of the product, then change your mind. Modifying a page clears its all-visible
-- bit; ROLLBACK does not set it again. (A DELETE adds no index entries, unlike an UPDATE, which
-- would leave page splits behind in every index on the table.)
BEGIN;
DELETE FROM order_lines WHERE product_id = 50;
ROLLBACK;
\echo '=== after a rolled-back DELETE of the same lines'
EXPLAIN (ANALYZE, BUFFERS) SELECT count(*), sum(quantity), sum(quantity * unit_price)
FROM order_lines WHERE product_id = 50;
VACUUM order_lines;
\echo '=== after VACUUM order_lines'
EXPLAIN (ANALYZE, BUFFERS) SELECT count(*), sum(quantity), sum(quantity * unit_price)
FROM order_lines WHERE product_id = 50;
DROP INDEX lab_order_lines_product_id_covering;
