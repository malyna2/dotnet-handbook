#!/usr/bin/env bash
# mine-git.sh — list story candidates from your own commit history.
#
# Usage:  ./mine-git.sh [--author <email-or-name>] [--since <date>] <repo> [<repo> ...]
#         --author  defaults to `git config user.email` of each repo
#         --since   defaults to "12 months ago" (anything `git log --since` accepts)
#
# Prints Markdown to stdout. It only reads local git history; nothing leaves your machine.
# The output names files, branches and commit subjects, so treat it as private.
#
# Portable on purpose: bash 3.2 (macOS) and POSIX awk (BSD awk, mawk, gawk).
# Output is truncated with `awk 'NR <= n'` rather than `head`: head exits early, git then
# dies of SIGPIPE, and under `set -o pipefail` that would abort the whole script.

# Backticks inside the --pretty formats are Markdown code spans, not command substitution.
# shellcheck disable=SC2016

set -eo pipefail

author=""
since="12 months ago"
repos=()

while [ $# -gt 0 ]; do
  case "$1" in
    --author) author="$2"; shift 2 ;;
    --since)  since="$2";  shift 2 ;;
    -h|--help) sed -n '2,9p' "$0"; exit 0 ;;
    -*) echo "unknown option: $1" >&2; exit 2 ;;
    *) repos+=("$1"); shift ;;
  esac
done

if [ ${#repos[@]} -eq 0 ]; then
  echo "usage: $0 [--author <email-or-name>] [--since <date>] <repo> [<repo> ...]" >&2
  exit 2
fi

# Commit subjects that usually hide a story: incidents, reversals, performance, correctness.
KEYWORDS='fix|hotfix|incident|outage|post-?mortem|revert|roll ?back|leak|deadlock|timeout|slow|perf|latency|race condition|racy|flaky|retry|idempot|duplicate|migrat|security|cve|vulnerab|data loss|corrupt'

for repo in "${repos[@]}"; do
  if ! git -C "$repo" rev-parse --git-dir >/dev/null 2>&1; then
    echo "skipping $repo: not a git repository" >&2
    continue
  fi

  name=$(basename "$(cd "$repo" && pwd)")
  who="$author"
  if [ -z "$who" ]; then
    who=$(git -C "$repo" config user.email || true)
  fi
  if [ -z "$who" ]; then
    echo "warning: no --author and no user.email in $repo; listing everyone's commits" >&2
  fi

  # Common git log arguments for "my commits in the window".
  set -- --no-merges --since="$since"
  if [ -n "$who" ]; then set -- "$@" --author="$who"; fi

  total=$(git -C "$repo" log "$@" --pretty=%h | wc -l | tr -d ' ')

  echo "# Story candidates — $name"
  echo
  echo "_Author: ${who:-everyone} · since: ${since} · commits: ${total}_"
  echo

  if [ "$total" -eq 0 ]; then
    echo "No commits found. Check --author (try your name instead of your email) and --since."
    echo
    continue
  fi

  echo "## Commits per month"
  echo
  echo "A month that is much busier or much quieter than its neighbours usually has a story: a launch, an incident, a migration — or a month spent unblocking other people."
  echo
  git -C "$repo" log "$@" --date=format:%Y-%m --pretty=%ad \
    | sort | uniq -c \
    | awk '{ bar = ""; for (i = 0; i < $1 && i < 60; i++) bar = bar "#"; printf "- %s %4d %s\n", $2, $1, bar }'
  echo

  echo "## Where you worked most"
  echo
  echo "Top directories by number of file changes. The areas you own are where your \"influence\" and \"tech debt\" stories live."
  echo
  git -C "$repo" log "$@" --name-only --pretty=format: \
    | awk 'NF { n = split($0, p, "/"); d = (n == 1) ? "(repo root)" : (n == 2 ? p[1] : p[1] "/" p[2]); c[d]++ }
           END { for (d in c) printf "%d\t%s\n", c[d], d }' \
    | sort -rn | awk 'NR <= 10' \
    | awk -F '\t' '{ printf "- %s — %d changes\n", $2, $1 }'
  echo

  echo "## Commits whose message suggests a story"
  echo
  echo "Incidents, reversals, performance and correctness work (keywords in the subject or body). For each: what broke, what did you decide, what number moved?"
  echo
  git -C "$repo" log "$@" -i -E --grep="$KEYWORDS" --date=short --pretty='- %ad `%h` %s' | awk 'NR <= 40'
  echo

  echo "## Your largest changes"
  echo
  echo "Lines added + deleted per commit. Big diffs are often migrations or rewrites — ask what made them necessary."
  echo
  git -C "$repo" log "$@" --date=short --pretty='@@%ad `%h` %s' --numstat \
    | awk '/^@@/ { if (h != "") printf "%d\t%s\n", s, h; h = substr($0, 3); s = 0; next }
           NF == 3 && $1 ~ /^[0-9]+$/ { s += $1 + $2 }
           END { if (h != "") printf "%d\t%s\n", s, h }' \
    | sort -rn | awk 'NR <= 10' \
    | awk -F '\t' '{ printf "- %s — %d line%s\n", $2, $1, ($1 == 1 ? "" : "s") }'
  echo

  echo "## Commits outside working hours"
  echo
  echo "Weekends and 22:00–06:00 (commit time zone). Often incidents or deadlines — both are stories."
  echo
  git -C "$repo" log "$@" --date=format:'%a %H' --pretty='%ad|%h|%s' \
    | awk -F '|' '{ split($1, t, " "); h = t[2] + 0;
                    if (t[1] == "Sat" || t[1] == "Sun" || h >= 22 || h < 6) { printf "- %s `%s` %s\n", $1, $2, $3; n++ } }
                  END { if (n == 0) print "- none" }' \
    | awk 'NR <= 20'
  echo

  echo "## Your commits that were later reverted"
  echo
  echo "A reverted change is the raw material of a \"bad decision\" story — if you can explain why it was reverted and what you do differently now."
  echo
  git -C "$repo" log "$@" --pretty=%H > "${TMPDIR:-/tmp}/mine-git.$$"
  git -C "$repo" log --since="$since" --grep='This reverts commit' --pretty='%H %s%n%b' \
    | awk -v mine="${TMPDIR:-/tmp}/mine-git.$$" '
        BEGIN { while ((getline l < mine) > 0) own[l] = 1 }
        /^[0-9a-f]{40} / { subj = substr($0, 42); next }
        /This reverts commit/ { for (i = 1; i <= NF; i++) { h = $i; sub(/\.$/, "", h);
            if (h in own) { printf "- `%s` was reverted by: %s\n", substr(h, 1, 7), subj; n++ } } }
        END { if (n == 0) print "- none found" }'
  rm -f "${TMPDIR:-/tmp}/mine-git.$$"
  echo
done

echo "---"
echo
echo "Next: pick the five candidates with the clearest *before and after*, and open a STAR worksheet for each."
