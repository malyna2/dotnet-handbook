#!/usr/bin/env bash
# SQL Server track. Needs:  docker compose --profile sqlserver up -d
#   ./sqlserver.sh seed               create and seed the shop database (~2 M orders)
#   ./sqlserver.sh 1                  run sqlserver/rung1.sql (and so on for 2, 3, 4)
set -euo pipefail
cd "$(dirname "$0")"
target="${1:?usage: $0 seed|1|2|3|4}"
case "$target" in
  seed) file=/lab/seed.sql ;;
  [1-4]) file="/lab/rung$target.sql" ;;
  *) echo "usage: $0 seed|1|2|3|4" >&2; exit 2 ;;
esac
run() {
  docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P 'Lab_Passw0rd!' -C -b -W -s "$(printf '\t')" -i "$file"
}
# Plans are wide; sqlserver/format.py condenses them. Without python3 you get sqlcmd's raw output.
if command -v python3 >/dev/null 2>&1; then run | python3 sqlserver/format.py; else run; fi
