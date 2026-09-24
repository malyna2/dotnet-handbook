#!/bin/bash
# SessionStart hook for Claude Code on the web.
#
# The Practice Gym labs (labs/, verify/) need the .NET 10 SDK and a running Docker
# daemon. Cloud containers start with neither, and the dotnet-install hosts
# (builds.dotnet.microsoft.com, dotnetcli.azureedge.net) are blocked there, so the
# SDK comes from Ubuntu's apt archive instead. Safe to run more than once.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

# --- .NET 10 SDK (cached with the container after the first install) ----------
if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  export DEBIAN_FRONTEND=noninteractive
  # Some third-party PPAs in the base image are unreachable; their failures are
  # warnings, and the Ubuntu archive that carries the SDK still updates.
  apt-get update -qq || true
  apt-get install -y -qq dotnet-sdk-10.0 >/dev/null
fi

if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
    echo 'export DOTNET_NOLOGO=1'
  } >> "$CLAUDE_ENV_FILE"
fi

# --- Docker daemon (processes are not cached, so start it every session) -----
if command -v dockerd >/dev/null 2>&1 && ! docker info >/dev/null 2>&1; then
  setsid nohup dockerd >/var/log/dockerd.log 2>&1 < /dev/null &
  for _ in $(seq 1 30); do
    if docker info >/dev/null 2>&1; then break; fi
    sleep 1
  done
fi

echo "dotnet: $(dotnet --version 2>/dev/null || echo 'not installed')"
if docker info >/dev/null 2>&1; then
  echo "docker: daemon running"
else
  echo "docker: daemon NOT running (see /var/log/dockerd.log)"
fi
