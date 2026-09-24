#!/usr/bin/env bash
# Seed the lab database.   Usage: ./seed.sh [small|medium|large]   (default: medium)
# Needs the compose stack up:  docker compose up -d
set -euo pipefail
cd "$(dirname "$0")"
scale="${1:-medium}"
case "$scale" in small|medium|large) ;; *) echo "usage: $0 [small|medium|large]" >&2; exit 2 ;; esac
docker compose exec -T postgres psql -U lab -d shop -v scale="$scale" -f /lab/seed.sql
