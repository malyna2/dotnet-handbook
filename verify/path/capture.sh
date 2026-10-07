#!/usr/bin/env bash
# Regenerates reference-runs/: the raw output behind every number a learning-path page quotes, each
# file headed with the environment it was measured on. Every experiment folder is captured:
#   - <Name>/capture.args, if present: one run per line, "<file> [args...]";
#     without it, one run into reference-runs/<name-in-kebab-case>.txt;
#   - <Name>/requires-docker marks an experiment that needs the services in docker-compose.yml.
# Start the services first, or skip those experiments:
#   ACCEPT_EULA=Y docker compose up -d && ACCEPT_EULA=Y ./capture.sh
#   ./capture.sh --no-docker
# Capture only some experiments (and leave the other files alone) with --only:
#   ./capture.sh --no-docker --only NPlusOne LogTemplate
set -euo pipefail
cd "$(dirname "$0")"

docker=1
only=()
args=("$@")
for ((i = 0; i < ${#args[@]}; i++)); do
  case "${args[i]}" in
    --no-docker) docker=0 ;;
    --only) only=("${args[@]:i+1}"); break ;;
    *) echo "unknown argument: ${args[i]}" >&2; exit 2 ;;
  esac
done
if [[ $docker == 1 && "${ACCEPT_EULA:-}" != "Y" ]]; then
  echo "Set ACCEPT_EULA=Y (the compose file needs it to read the running services), or pass --no-docker." >&2
  exit 2
fi
[[ $docker == 1 ]] && export ACCEPT_EULA

dotnet build Tests/LearningPath.Tests.csproj -c Release -nologo -v quiet -warnaserror
mkdir -p reference-runs

cpu=$(grep -m1 "model name" /proc/cpuinfo | cut -d: -f2 | xargs)
ram=$(awk '/MemTotal/ { printf "%.0f GB", $2 / 1048576 }' /proc/meminfo)
os=$(sed -n 's/^PRETTY_NAME="\(.*\)"$/\1/p' /etc/os-release)
runtime=$(dotnet --list-runtimes | awk '/Microsoft.NETCore.App/ { v = $2 } END { print v }')
header="# Environment: $cpu, $(nproc) vCPU, $ram RAM, $os, .NET runtime $runtime, SDK $(dotnet --version)"
services=""
if [[ $docker == 1 ]]; then
  sql=$(docker compose exec -T mssql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Emulator-Only-Passw0rd!' -C -h -1 \
    -Q "SET NOCOUNT ON; SELECT CONCAT(CONVERT(varchar(128), SERVERPROPERTY('ProductVersion')), ' ', CONVERT(varchar(128), SERVERPROPERTY('ProductUpdateLevel')), ', collation ', CONVERT(varchar(128), SERVERPROPERTY('Collation')))" | xargs)
  header="$header, Docker $(docker version --format '{{.Server.Version}}')"
  services="# Services: SQL Server $sql; Service Bus emulator $(docker compose images servicebus --format json | python3 -c 'import json,sys; print(json.load(sys.stdin)[0]["Tag"])')"
fi

TIMEFORMAT='[process time: wall %R s, user CPU %U s, system CPU %S s]'
run() {   # run <file> <experiment> [args...]
  local file=$1; shift
  { echo "$header"; [[ -n "$services" && -f "$1/requires-docker" ]] && echo "$services"
    echo "# Captured: $(date -u '+%Y-%m-%d %H:%M UTC') by capture.sh"
    echo "# Program: $1, Release build${2:+, arguments: ${*:2}}"; echo
    set +e; time dotnet "$1/bin/Release/net10.0/$1.dll" "${@:2}" 2>&1; echo "[exit code $?]"; set -e
  } > "reference-runs/$file.txt" 2>&1
  echo "reference-runs/$file.txt"
}

for dir in */; do
  name=${dir%/}
  [[ -f "$name/Program.cs" ]] || continue
  if (( ${#only[@]} )) && [[ ! " ${only[*]} " == *" $name "* ]]; then continue; fi
  if [[ -f "$name/requires-docker" && $docker == 0 ]]; then echo "skipped $name (needs Docker)"; continue; fi
  if [[ -f "$name/capture.args" ]]; then
    while read -r file args; do
      [[ -z "$file" || "$file" == \#* ]] && continue
      # shellcheck disable=SC2086
      run "$file" "$name" $args
    done < "$name/capture.args"
  else
    run "$(sed -E 's/([a-z0-9])([A-Z])/\1-\2/g' <<< "$name" | tr '[:upper:]' '[:lower:]')" "$name"
  fi
done

if [[ $docker == 1 ]] && (( ! ${#only[@]} )); then
  { echo "$header"; echo "$services"; echo "# Captured: $(date -u '+%Y-%m-%d %H:%M UTC') by capture.sh"
    echo "# Command: sqlcmd -d tempdb -i collation.sql, inside the mssql container"; echo
    docker compose exec -T mssql /opt/mssql-tools18/bin/sqlcmd -S localhost -d tempdb -U sa -P 'Emulator-Only-Passw0rd!' \
      -C -W -s '|' -i /dev/stdin < collation.sql
  } > reference-runs/collation.txt
  echo "reference-runs/collation.txt"
fi
