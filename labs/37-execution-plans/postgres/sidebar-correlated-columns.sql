-- Chapter 37 sidebar: a 5x misestimate that doesn't matter. Country and city are correlated
-- (Berlin only exists in DE), but the planner multiplies their selectivities as if they were not.
\set ON_ERROR_STOP on
\echo '=== without extended statistics'
EXPLAIN (ANALYZE, BUFFERS)
SELECT c.id, c.name, count(*) AS orders_last_quarter
FROM orders o JOIN customers c ON c.id = o.customer_id
WHERE o.created_at >= timestamptz '2026-06-01' AND c.country = 'DE' AND c.city = 'Berlin'
GROUP BY c.id, c.name ORDER BY c.id;
CREATE STATISTICS lab_customers_country_city (dependencies, mcv) ON country, city FROM customers;
ANALYZE customers;
\echo '=== with CREATE STATISTICS lab_customers_country_city (dependencies, mcv) ON country, city'
EXPLAIN (ANALYZE, BUFFERS)
SELECT c.id, c.name, count(*) AS orders_last_quarter
FROM orders o JOIN customers c ON c.id = o.customer_id
WHERE o.created_at >= timestamptz '2026-06-01' AND c.country = 'DE' AND c.city = 'Berlin'
GROUP BY c.id, c.name ORDER BY c.id;
DROP STATISTICS lab_customers_country_city;
ANALYZE customers;
