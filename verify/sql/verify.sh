#!/usr/bin/env bash
# Runs every SQL query Chapter 7 prints (queries/) and every query behind a claim in its prose (claims/)
# on PostgreSQL 17 and SQL Server 2022, rewrites reference-runs/ with the raw output, and fails if
# the engines disagree where the chapter says they agree, or a printed query is not in the chapter.
#   ./verify.sh          start the containers, check, leave them running
#   ./verify.sh --down   ...and remove the containers afterwards
# Setting ACCEPT_EULA=Y here accepts the SQL Server EULA (Developer edition, local use only).
set -euo pipefail
cd "$(dirname "$0")"
export ACCEPT_EULA=Y
compose=(docker compose -f docker-compose.yml)

"${compose[@]}" up -d --quiet-pull
for _ in $(seq 1 60); do
  if "${compose[@]}" exec -T postgres pg_isready -U postgres -q 2>/dev/null &&
     "${compose[@]}" exec -T mssql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Local-Only-Passw0rd!' -C \
       -Q "SELECT 1" >/dev/null 2>&1; then
    ready=1; break
  fi
  sleep 2
done
[[ "${ready:-}" == 1 ]] || { echo "the engines did not become ready within two minutes" >&2; exit 1; }

cpu=$(grep -m1 "model name" /proc/cpuinfo | cut -d: -f2 | xargs)
ram=$(awk '/MemTotal/ { printf "%.0f GB", $2 / 1048576 }' /proc/meminfo)
pg=$("${compose[@]}" exec -T postgres psql -U postgres -At -c "SHOW server_version")
ms=$("${compose[@]}" exec -T mssql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Local-Only-Passw0rd!' -C -h -1 \
  -Q "SET NOCOUNT ON; SELECT CONCAT(CONVERT(varchar(128), SERVERPROPERTY('ProductVersion')), ' ', CONVERT(varchar(128), SERVERPROPERTY('ProductUpdateLevel')), ', collation ', CONVERT(varchar(128), SERVERPROPERTY('Collation')))" | xargs)
export SQL_ENV_HEADER="# Environment: $cpu, $(nproc) vCPU, $ram RAM, Docker $(docker version --format '{{.Server.Version}}')
# Engines: PostgreSQL $pg; SQL Server $ms
# Data: schema.sql (4 customers, 8 orders, 10 order lines, 4 categories), loaded fresh before the run
# Captured: $(date -u '+%Y-%m-%d %H:%M UTC') by verify.sh"

status=0
python3 run.py || status=$?

# The EF Core side of the chapter's LINQ table: the SQL EF Core 10 sends, and its rows, on both engines.
dotnet build EfLinq/EfLinq.csproj -c Release -nologo -v quiet
{ echo "$SQL_ENV_HEADER"; echo "# Program: EfLinq, Release build, .NET runtime $(dotnet --list-runtimes | awk '/Microsoft.NETCore.App/ { v = $2 } END { print v }')"; echo
  dotnet EfLinq/bin/Release/net10.0/EfLinq.dll; } > reference-runs/ef-linq.txt 2>&1 || { echo "FAIL EfLinq did not run"; status=1; }
python3 - <<'EOF' || status=1
import re, sys
out = open("reference-runs/ef-linq.txt").read()
mssql, pg = out.split("===== PostgreSQL")
checks = [  # (what the chapter says, SQL Server pattern, PostgreSQL pattern)
    ("!= adds OR ... IS NULL", r"<> 'Kyiv' OR \[c\]\.\[City\] IS NULL", r"<> 'Kyiv' OR c\.city IS NULL"),
    ("!Any() is NOT EXISTS", r"NOT EXISTS", r"NOT EXISTS"),
    ("!Contains is NULL-safe", r"!Contains over a subquery\n(?:.*\n)*?WHERE NOT EXISTS", r"NOT \(COALESCE\(c\.id IN"),
    ("Sum of nothing is 0", r"Name = Dan, Orders = 0, Revenue = 0", r"Name = Dan, Orders = 0, Revenue = 0"),
    ("Where after GroupBy is HAVING", r"HAVING COUNT\(\*\) > 1", r"HAVING count\(\*\)::int > 1"),
    ("LeftJoin is LEFT JOIN", r"LEFT JOIN \[Orders\] AS \[o\] ON", r"LEFT JOIN orders AS o ON"),
    ("filtered navigation keeps Dan", r"filtered navigation(?:.*\n)*?  \{ Name = Dan, OrderId =  \}", r"filtered navigation(?:.*\n)*?  \{ Name = Dan, OrderId =  \}"),
    ("Take(2) per navigation is ROW_NUMBER",
     r"ROW_NUMBER\(\) OVER\(PARTITION BY \[o\]\.\[CustomerId\] ORDER BY \[o\]\.\[Total\] DESC, \[o\]\.\[Id\]\)",
     r"ROW_NUMBER\(\) OVER\(PARTITION BY o\.customerid ORDER BY o\.total DESC, o\.id\)"),
    ("top 2 per customer", r"Ada: 10, 11\n  Ben: 13, 14\n  Cleo: 15", r"Ada: 10, 11\n  Ben: 13, 14\n  Cleo: 15"),
]
bad = 0
for what, m, p in checks:
    for engine, text, pat in (("SQL Server", mssql, m), ("PostgreSQL", pg, p)):
        ok = re.search(pat, text) is not None
        bad += not ok
        print(f"{'ok  ' if ok else 'FAIL'} EF Core on {engine}: {what}")
sys.exit(1 if bad else 0)
EOF
[[ "${1:-}" == "--down" ]] && "${compose[@]}" down
exit $status
