#!/usr/bin/env bash
# Regenerates reference-runs/: the raw output behind every number STUDY_TRACK.md quotes, each file
# with the environment it was measured on. Start the services first (verify.sh's README section):
#   ACCEPT_EULA=Y docker compose up -d && ACCEPT_EULA=Y ./capture.sh
set -euo pipefail
cd "$(dirname "$0")"

if [[ "${ACCEPT_EULA:-}" != "Y" ]]; then
  echo "Set ACCEPT_EULA=Y (the compose file needs it to read the running services)." >&2
  exit 2
fi
export ACCEPT_EULA

dotnet build StudyTrack.slnx -c Release -nologo -v quiet -warnaserror
mkdir -p reference-runs

cpu=$(grep -m1 "model name" /proc/cpuinfo | cut -d: -f2 | xargs)
ram=$(awk '/MemTotal/ { printf "%.0f GB", $2 / 1048576 }' /proc/meminfo)
os=$(sed -n 's/^PRETTY_NAME="\(.*\)"$/\1/p' /etc/os-release)
runtime=$(dotnet --list-runtimes | awk '/Microsoft.NETCore.App/ { v = $2 } END { print v }')
sql=$(docker compose exec -T mssql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Emulator-Only-Passw0rd!' -C -h -1 \
  -Q "SET NOCOUNT ON; SELECT CONCAT(CONVERT(varchar(128), SERVERPROPERTY('ProductVersion')), ' ', CONVERT(varchar(128), SERVERPROPERTY('ProductUpdateLevel')), ', collation ', CONVERT(varchar(128), SERVERPROPERTY('Collation')))" | xargs)
header="# Environment: $cpu, $(nproc) vCPU, $ram RAM, $os, .NET runtime $runtime, SDK $(dotnet --version), Docker $(docker version --format '{{.Server.Version}}')
# Services: SQL Server $sql; Service Bus emulator $(docker compose images servicebus --format json | python3 -c 'import json,sys; print(json.load(sys.stdin)[0]["Tag"])')
# Captured: $(date -u '+%Y-%m-%d %H:%M UTC') by capture.sh"

TIMEFORMAT='[process time: wall %R s, user CPU %U s, system CPU %S s]'
run() {   # run <file> <experiment> [args...]
  local file=$1; shift
  { echo "$header"; echo "# Program: $1, Release build${2:+, arguments: ${*:2}}"; echo
    set +e; time dotnet "$1/bin/Release/net10.0/$1.dll" "${@:2}" 2>&1; echo "[exit code $?]"; set -e
  } > "reference-runs/$file.txt" 2>&1
  echo "reference-runs/$file.txt"
}

run async-void AsyncVoid
run when-all WhenAll
run starvation-await Starvation await
run starvation-block Starvation block
run httpclient-per-request HttpClientPerRequest
run peek-lock PeekLock
run seek-vs-scan SeekVsScan
run check-then-act CheckThenAct

{ echo "$header"; echo "# Command: sqlcmd -d tempdb -i collation.sql, inside the mssql container"; echo
  docker compose exec -T mssql /opt/mssql-tools18/bin/sqlcmd -S localhost -d tempdb -U sa -P 'Emulator-Only-Passw0rd!' \
    -C -W -s '|' -i /dev/stdin < collation.sql
} > reference-runs/collation.txt
echo "reference-runs/collation.txt"
