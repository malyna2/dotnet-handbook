#!/usr/bin/env bash
# Regenerates every file in reference-runs/ — the raw output behind the numbers in Chapter 37.
# Needs:  docker compose --profile sqlserver up -d  &&  ./seed.sh medium  &&  ./sqlserver.sh seed
set -euo pipefail
cd "$(dirname "$0")/.."

header() {
  echo "date        : $(date -u '+%Y-%m-%d %H:%M') UTC"
  echo "machine     : $(nproc) vCPU ($(grep -m1 'model name' /proc/cpuinfo | cut -d: -f2 | xargs)), $(awk '/MemTotal/ {printf "%.0f GB RAM", $2/1024/1024}' /proc/meminfo)"
  echo "$1"
  echo
}

pg_header() {
  header "server      : $(docker compose exec -T postgres psql -U lab -d shop -Atc 'select version()')"
}
lab_connection="Host=localhost;Port=5433;Username=lab;Password=lab;Database=shop"

echo "== the seeded baseline: relation sizes and shared_buffers"
{
  pg_header
  docker compose exec -T postgres psql -U lab -d shop -X -P pager=off \
    -c "SELECT c.relname AS relation, CASE c.relkind WHEN 'r' THEN 'table' ELSE 'index' END AS kind,
               pg_size_pretty(pg_relation_size(c.oid)) AS size, pg_relation_size(c.oid) / 8192 AS pages
          FROM pg_class c
         WHERE c.relnamespace = 'public'::regnamespace AND c.relkind IN ('r', 'i')
         ORDER BY coalesce((SELECT i.indrelid FROM pg_index i WHERE i.indexrelid = c.oid), c.oid), c.relkind DESC, c.relname" \
    -c "SHOW shared_buffers"
} > reference-runs/postgres-baseline.txt

since=$(date -u '+%Y-%m-%dT%H:%M:%SZ')
for impl in starter solution; do
  echo "== QueryLab, $impl"
  dotnet run --project src/QueryLab.Cli -c Release --property:Rungs="$impl" -- all --runs 5 \
    > "reference-runs/postgres-$impl.md"
done

echo "== auto_explain: what the server logged during both runs"
{
  pg_header
  # Keep each "duration: ... plan:" entry and its tab-indented plan lines.
  docker compose logs --no-log-prefix --since "$since" postgres \
    | awk '/ LOG:  duration: .* plan:/ { p = 1; print; next } /^\t/ { if (p) print; next } { p = 0 }'
} > reference-runs/postgres-auto-explain.txt

echo "== rung 1 (starter) as pg_stat_statements sees it"
dotnet run --project src/QueryLab.Cli -c Release --property:Rungs=starter -- 1 --runs 5 --no-plan > /dev/null
{
  pg_header
  docker compose exec -T postgres psql -U lab -d shop -X -P pager=off -c "
    SELECT calls, rows, round(total_exec_time::numeric, 2) AS total_ms,
           shared_blks_hit + shared_blks_read AS buffers,
           left(regexp_replace(query, '\s+', ' ', 'g'), 70) AS query
    FROM pg_stat_statements
    WHERE query ~* '^select' AND query !~* 'pg_stat_statements|pg_indexes|version\(\)'
    ORDER BY calls DESC"
} > reference-runs/postgres-rung1-pg-stat-statements.txt

echo "== rung 9 (starter) with plan_cache_mode=force_custom_plan"
dotnet run --project src/QueryLab.Cli -c Release --property:Rungs=starter -- 9 --runs 5 \
  --connection "$lab_connection;Options=-c plan_cache_mode=force_custom_plan" \
  > reference-runs/postgres-rung9-force-custom-plan.md

for script in sidebar-correlated-columns sidebar-keyset-forms break-it-stale-statistics break-it-visibility-map; do
  echo "== $script"
  {
    pg_header
    docker compose exec -T postgres psql -U lab -d shop -X -q -P pager=off -f "/lab/$script.sql"
  } > "reference-runs/postgres-${script#sidebar-}.txt"
done

for n in 1 2 3 4; do
  echo "== SQL Server rung $n"
  {
    header "server      : $(docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Lab_Passw0rd!' -C -h -1 -W -Q 'SET NOCOUNT ON; SELECT @@VERSION' | head -1)"
    ./sqlserver.sh "$n"
  } > "reference-runs/sqlserver-rung$n.txt"
done
echo "done"
