#!/usr/bin/env bash
# Chapter 62 — the automated half of a .NET health check.
#
# Clones the target at a pinned commit into .work/ (unless --target points at a checkout you
# already have), then runs the checks that a machine can do and writes raw evidence to an
# output directory. It does NOT write findings: turning evidence into findings is your job.
#
#   scripts/health-check.sh                         # dotnet/eShop at the pinned commit -> .work/out
#   scripts/health-check.sh --out /tmp/hc           # choose the output directory
#   scripts/health-check.sh --target ~/src/my-app   # an existing local checkout (nothing is cloned)
#   scripts/health-check.sh --repo URL --commit SHA # another public repository
#   scripts/health-check.sh --no-dotnet             # skip restore and package checks (fast, offline-ish)
#   scripts/health-check.sh --build                 # also build the solution and run the unit-test projects
#
# Needs bash, git, grep, awk, sort. Uses when present: dotnet (SDK 10), curl, jq, rg.
# Every section is tolerant: a missing tool writes "SKIPPED: <reason>" and the run continues.
set -uo pipefail

KIT="$(cd "$(dirname "$0")/.." && pwd)"
REPO_URL="https://github.com/dotnet/eShop.git"
COMMIT="b4a40872005d4bb29e5b1fa1ff7e244143d39215"   # main, 2026-08-28 ("Clarify Aspire setup and deployment (#1019)")
SOLUTION="eShop.Web.slnf"                            # the web projects; the MAUI apps need workloads
TARGET=""
OUT=""
RUN_DOTNET=1
RUN_BUILD=0
CHURN_DAYS=365

while [ $# -gt 0 ]; do
  case "$1" in
    --repo)      REPO_URL="$2"; shift 2 ;;
    --commit)    COMMIT="$2"; shift 2 ;;
    --target)    TARGET="$2"; shift 2 ;;
    --solution)  SOLUTION="$2"; shift 2 ;;
    --out)       OUT="$2"; shift 2 ;;
    --no-dotnet) RUN_DOTNET=0; shift ;;
    --build)     RUN_BUILD=1; shift ;;
    --churn-days) CHURN_DAYS="$2"; shift 2 ;;
    -h|--help)   sed -n '2,16p' "$0"; exit 0 ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done

have() { command -v "$1" >/dev/null 2>&1; }
xa() { tr '\n' '\0' | xargs -0 -r "$@"; }   # file names may contain spaces (eShop has one)
note() { printf '== %s\n' "$*" >&2; }

# ---------------------------------------------------------------- target
if [ -z "$TARGET" ]; then
  name="$(basename "$REPO_URL" .git)"
  TARGET="$KIT/.work/$name"
  if [ ! -d "$TARGET/.git" ]; then
    note "cloning $REPO_URL into .work/$name"
    mkdir -p "$KIT/.work"
    git clone --quiet "$REPO_URL" "$TARGET" || { echo "clone failed" >&2; exit 1; }
  fi
  if ! git -C "$TARGET" cat-file -e "$COMMIT^{commit}" 2>/dev/null; then
    git -C "$TARGET" fetch --quiet origin || true
  fi
  git -C "$TARGET" -c advice.detachedHead=false checkout --quiet "$COMMIT" \
    || { echo "cannot check out $COMMIT" >&2; exit 1; }
fi
TARGET="$(cd "$TARGET" && pwd)"
OUT="${OUT:-$KIT/.work/out}"
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
HEAD="$(git -C "$TARGET" rev-parse HEAD 2>/dev/null || echo unknown)"
HEAD_TS="$(git -C "$TARGET" log -1 --format=%ct 2>/dev/null || date +%s)"
cd "$TARGET" || exit 1

# Paths in the output are relative to the target, so two machines produce comparable files.
rel() { sed -e "s#$TARGET/##g"; }

# Source files, excluding build output and vendored front-end code.
cs_files() {
  git ls-files '*.cs' | grep -v -E '(^|/)(obj|bin|artifacts|node_modules|wwwroot)/' | grep -v -E '(Migrations/|\.Designer\.cs$|\.g\.cs$)'
}
src_files() { cs_files | grep -v -i -E '(^|/)tests?/|Tests?/'; }  # production code only
count() { # count <regex> over production C# -> "<n> matches in <m> files"
  local n m
  n=$(src_files | xa grep -E -o -- "$1" 2>/dev/null | wc -l | tr -d ' ')
  m=$(src_files | xa grep -E -l -- "$1" 2>/dev/null | wc -l | tr -d ' ')
  printf '%6s matches in %4s files   %s\n' "$n" "$m" "$1"
}
where() { # where <regex> -> file:line list (first 15)
  src_files | xa grep -E -n -- "$1" 2>/dev/null | head -15 | sed 's/^/    /'
}

# ---------------------------------------------------------------- 00 environment
note "00 environment"
{
  echo "date        : $(date -u '+%Y-%m-%d %H:%M') UTC"
  echo "machine     : $(nproc 2>/dev/null) vCPU ($(grep -m1 'model name' /proc/cpuinfo 2>/dev/null | cut -d: -f2 | xargs)), $(awk '/MemTotal/ {printf "%.0f GB RAM", $2/1024/1024}' /proc/meminfo 2>/dev/null)"
  echo "os          : $( (. /etc/os-release 2>/dev/null && echo "$PRETTY_NAME") || uname -sr)"
  echo "git         : $(git --version | awk '{print $3}')"
  if have dotnet; then
    # Ask from outside the target so its global.json can't hide the installed SDKs.
    echo "dotnet sdks : $(cd / && dotnet --list-sdks 2>/dev/null | awk '{print $1}' | paste -sd' ' -)"
  else
    echo "dotnet sdks : SKIPPED (dotnet not on PATH)"
  fi
  echo "target      : $(git remote get-url origin 2>/dev/null || echo "$TARGET")"
  echo "commit      : $HEAD ($(git log -1 --format='%cd' --date=short 2>/dev/null))"
  echo "history     : $(git rev-list --count HEAD 2>/dev/null) commits since $(git log --reverse --format=%cd --date=short 2>/dev/null | head -1)"
} > "$OUT/00-environment.txt"

# ---------------------------------------------------------------- 01 inventory + runtime/EOL
note "01 inventory and runtime"
{
  echo "## Projects (git ls-files '*.csproj'): $(git ls-files '*.csproj' | wc -l | tr -d ' ')"
  git ls-files '*.csproj' | sed 's/^/  /'
  echo
  echo "## Solutions"
  git ls-files '*.sln' '*.slnx' '*.slnf' | sed 's/^/  /'
  echo
  echo "## Target frameworks (per project file, plus Directory.Build.props)"
  git ls-files '*.csproj' '*.props' '*.targets' | xa grep -ho '<TargetFrameworks\?>[^<]*' 2>/dev/null \
    | sed 's/<TargetFrameworks\?>//' | sort | uniq -c | sort -rn
  echo
  echo "## global.json"
  cat global.json 2>/dev/null || echo "  (none)"
  echo
  echo "## SDK resolution from inside the repo"
  if have dotnet; then
    if dotnet --version >/dev/null 2>&1; then echo "  resolves to $(dotnet --version)"
    else echo "  FAILS: no installed SDK satisfies global.json (installed: $(cd / && dotnet --list-sdks | awk '{print $1}' | paste -sd' ' -))"
         echo "  (this script runs dotnet from outside the repo, which ignores global.json's sdk pin; say so in the report)"
    fi
  else echo "  SKIPPED: dotnet not on PATH"; fi
  echo
  echo "## Support status (dotnet/core releases-index.json)"
  if have curl && have jq; then
    curl -fsS --max-time 30 https://raw.githubusercontent.com/dotnet/core/main/release-notes/releases-index.json \
      | jq -r '."releases-index"[] | [."channel-version", ."support-phase", ."release-type", (."eol-date" // "-"), ."latest-runtime", ."latest-sdk"] | @tsv' \
      | awk 'BEGIN{print "  channel\tphase\ttype\teol-date\tlatest-runtime\tlatest-sdk"} {print "  "$0}' \
      || echo "  SKIPPED: could not fetch releases-index.json"
    echo
    echo "## Recent patches for the channels this repo targets (security = the patch fixes CVEs)"
    for ch in $(git ls-files '*.csproj' '*.props' | xa grep -ho -E 'net[0-9]+\.[0-9]+' 2>/dev/null | sed 's/^net//' | sort -u); do
      curl -fsS --max-time 30 "https://raw.githubusercontent.com/dotnet/core/main/release-notes/$ch/releases.json" \
        | jq -r --arg ch "$ch" '.releases[:4][] | "  \($ch)\t\(."release-version")\t\(."release-date")\tsecurity=\(.security)\tCVEs=\([."cve-list"[]?] | length)"' \
        || echo "  $ch: SKIPPED (could not fetch releases.json)"
    done
  else echo "  SKIPPED: needs curl and jq"; fi
} > "$OUT/01-inventory-runtime.txt" 2>&1

# ---------------------------------------------------------------- 02 dependencies
note "02 dependencies (restore + dotnet list package)"
pkgsum() { # pkgsum <json> -> "projects, unique packages, rows"
  jq -r '[.projects[] | .path as $p | (.frameworks // [])[] | ((.topLevelPackages // []) + (.transitivePackages // []))[] | .id] as $all
         | "rows: \($all | length)   unique packages: \($all | unique | length)"' "$1" 2>/dev/null
}
if [ "$RUN_DOTNET" = 1 ] && have dotnet; then
  sln="$SOLUTION"; [ -e "$sln" ] || sln="$(git ls-files '*.slnx' '*.sln' | head -1)"
  # Run from outside the repo: the host then ignores global.json's sdk pin (see 01). Record it.
  ( cd "$OUT" && dotnet restore "$TARGET/$sln" -nologo > "$OUT/02-restore.raw" 2>&1 ); rc=$?
  rel < "$OUT/02-restore.raw" > "$OUT/02-restore.log"; rm -f "$OUT/02-restore.raw"; echo "exit code: $rc" >> "$OUT/02-restore.log"
  for kind in outdated vulnerable deprecated; do
    ( cd "$OUT" && dotnet list "$TARGET/$sln" package --"$kind" --include-transitive --format json ) \
      2> "$OUT/02-packages-$kind.err" | rel > "$OUT/02-packages-$kind.json"
    [ -s "$OUT/02-packages-$kind.err" ] || rm -f "$OUT/02-packages-$kind.err"
  done
  ( cd "$OUT" && dotnet list "$TARGET/$sln" package --include-transitive --format json ) 2>/dev/null | rel > "$OUT/02-packages-all.json"
  {
    echo "solution: $sln"
    echo "restore : $(tail -1 "$OUT/02-restore.log")"
    if have jq; then
      for kind in all outdated vulnerable deprecated; do printf '%-11s %s\n' "$kind:" "$(pkgsum "$OUT/02-packages-$kind.json")"; done
      echo "projects: $(jq '.projects | length' "$OUT/02-packages-all.json")   unique top-level: $(jq '[.projects[] | (.frameworks // [])[] | (.topLevelPackages // [])[] | .id] | unique | length' "$OUT/02-packages-all.json")   unique transitive: $(jq '[.projects[] | (.frameworks // [])[] | (.transitivePackages // [])[] | .id] | unique | length' "$OUT/02-packages-all.json")"
      echo "(rows count one line per project and framework, so a package used by 10 projects is 10 rows)"
      echo
      echo "## Top-level packages behind the latest version (unique)"
      jq -r '[.projects[] | (.frameworks // [])[] | (.topLevelPackages // [])[]
              | select(.latestVersion != null and .latestVersion != .resolvedVersion)
              | "\(.id)\t\(.resolvedVersion) -> \(.latestVersion)"] | unique[]' "$OUT/02-packages-outdated.json" | sed 's/^/  /'
      echo
      echo "## Top-level upgrades by size (major / minor / patch / latest not found without --include-prerelease)"
      jq -r '[.projects[] | (.frameworks // [])[] | (.topLevelPackages // [])[]
              | select(.latestVersion != null and .latestVersion != .resolvedVersion)
              | {id, r: .resolvedVersion, l: .latestVersion}] | unique[] | "\(.r) \(.l)"' "$OUT/02-packages-outdated.json" \
        | awk '{ split($1, a, /[.-]/); split($2, b, /[.-]/);
                 k = ($2 !~ /^[0-9]/) ? "not-found" : (a[1] != b[1]) ? "major" : (a[2] != b[2]) ? "minor" : "patch"; n[k]++ }
               END { printf "  major %d, minor %d, patch %d, not-found %d\n", n["major"], n["minor"], n["patch"], n["not-found"] }'
      echo
      echo "## Transitive packages behind the latest version: $(jq '[.projects[] | (.frameworks // [])[] | (.transitivePackages // [])[] | select(.latestVersion != .resolvedVersion) | .id] | unique | length' "$OUT/02-packages-outdated.json") unique"
      echo
      echo "## Vulnerable (unique id@version, severity)"
      jq -r '[.projects[] | (.frameworks // [])[] | ((.topLevelPackages // []) + (.transitivePackages // []))[]
              | "\(.id)@\(.resolvedVersion)\t\([.vulnerabilities[]?.severity] | join(","))"] | unique[]' "$OUT/02-packages-vulnerable.json" | sed 's/^/  /'
      echo "  (end)"
      echo
      echo "## Deprecated (unique)"
      jq -r '[.projects[] | (.frameworks // [])[] | ((.topLevelPackages // []) + (.transitivePackages // []))[]
              | "\(.id)@\(.resolvedVersion)\t\(.deprecationReasons // [] | join(","))\t alt: \(.alternativePackage.id // "-")"] | unique[]' "$OUT/02-packages-deprecated.json" | sed 's/^/  /'
      echo "  (end)"
      echo
      echo "## Prerelease versions in use (top-level, unique)"
      jq -r '[.projects[] | (.frameworks // [])[] | (.topLevelPackages // [])[] | select(.resolvedVersion | test("-")) | "\(.id)\t\(.resolvedVersion)"] | unique[]' "$OUT/02-packages-all.json" | sed 's/^/  /'
      echo "  (end)"
    else
      echo "SKIPPED summary: jq not on PATH (the raw JSON files are still there)"
    fi
  } > "$OUT/02-dependencies-summary.txt"
else
  echo "SKIPPED: --no-dotnet given or dotnet not on PATH" > "$OUT/02-dependencies-summary.txt"
fi

# ---------------------------------------------------------------- 03 supply chain
note "03 supply chain"
{
  echo "## NuGet sources and source mapping (nuget.config)"
  for f in $(git ls-files | grep -i -E '(^|/)nuget\.config$'); do echo "  -- $f"; sed 's/^/  /' "$f"; echo; done
  [ -z "$(git ls-files | grep -i -E '(^|/)nuget\.config$')" ] && echo "  (no nuget.config: machine-level sources apply)"
  echo
  echo "## Central package management / lock files / audit switches"
  for s in ManagePackageVersionsCentrally CentralPackageTransitivePinningEnabled RestorePackagesWithLockFile NuGetAudit NuGetAuditMode NuGetAuditLevel TreatWarningsAsErrors WarningsAsErrors NoWarn; do
    git ls-files '*.props' '*.targets' '*.csproj' | xa grep -H -n "<$s>" 2>/dev/null | sed 's/^/  /'
  done
  echo "  packages.lock.json files: $(git ls-files '*packages.lock.json' | wc -l | tr -d ' ')"
  echo
  echo "## GitHub Actions references (pinned to a SHA or to a moving tag?)"
  if ls .github/workflows/*.y*ml >/dev/null 2>&1; then
    grep -h -E '^\s*-?\s*uses:' .github/workflows/*.y*ml | sed -E 's/.*uses:\s*//; s/[[:space:]]+$//' | tr -d '\r' | sort | uniq -c | sed 's/^/  /'
    echo "  pinned to a full SHA: $(grep -h -E 'uses:\s*\S+@[0-9a-f]{40}' .github/workflows/*.y*ml | wc -l | tr -d ' ') of $(grep -h -E 'uses:' .github/workflows/*.y*ml | wc -l | tr -d ' ')"
    echo "  workflow permissions blocks: $(grep -l -E '^\s*permissions:' .github/workflows/*.y*ml 2>/dev/null | wc -l | tr -d ' ') of $(ls .github/workflows/*.y*ml | wc -l | tr -d ' ') workflows"
  else echo "  (no .github/workflows)"; fi
  echo "  dependabot/renovate config: $(git ls-files | grep -E '(dependabot\.ya?ml|renovate\.json)$' | paste -sd' ' - | sed 's/^$/none/')"
  echo
  echo "## Container base images (Dockerfile FROM lines)"
  df="$(git ls-files | grep -E '(^|/)Dockerfile[^/]*$')"
  if [ -n "$df" ]; then echo "$df" | xa grep -H -n '^FROM' | sed 's/^/  /'; else echo "  (no Dockerfiles: check for SDK container publish, <ContainerBaseImage>, or AppHost)"; fi
  git ls-files '*.csproj' '*.props' | xa grep -H -n -E '<Container[A-Za-z]*>' 2>/dev/null | sed 's/^/  /'
} > "$OUT/03-supply-chain.txt" 2>&1

# ---------------------------------------------------------------- 04 hotspots
note "04 churn x complexity hotspots"
since=$((HEAD_TS - CHURN_DAYS * 86400))
hotspots() { # hotspots <since-epoch> <label>
  echo "# churn = commits touching the file, $2 (up to $(date -u -d "@$HEAD_TS" +%F 2>/dev/null || echo "@$HEAD_TS"))"
  echo "# branches = count of if/case/for/foreach/while/catch/&&/||/?? tokens (a cheap cyclomatic proxy, Ch 30)"
  echo "# score = churn x branches; renames are not followed; migrations and generated files excluded"
  printf 'score\tchurn\tbranches\tlines\tauthors\tfile\n'
  local win=(--until="@$HEAD_TS"); [ "$1" -gt 0 ] && win+=(--since="@$1")
  git log "${win[@]}" --name-only --pretty=format: HEAD -- '*.cs' \
    | grep -v '^$' | sort | uniq -c | while read -r churn file; do
      [ -f "$file" ] || continue
      case "$file" in */obj/*|*/bin/*|*/Migrations/*|*.Designer.cs|*.g.cs) continue ;; esac
      lines=$(grep -c -v '^\s*$' "$file")
      branches=$(grep -o -E '\b(if|case|for|foreach|while|catch)\b|&&|\|\||\?\?' "$file" | wc -l | tr -d ' ')
      authors=$(git log "${win[@]}" --format=%ae HEAD -- "$file" | sort -u | wc -l | tr -d ' ')
      printf '%s\t%s\t%s\t%s\t%s\t%s\n' "$((churn * branches))" "$churn" "$branches" "$lines" "$authors" "$file"
    done | sort -t"$(printf '\t')" -k1,1nr -k2,2nr | head -25
}
hotspots "$since" "in the $CHURN_DAYS days before the pinned commit" > "$OUT/04-hotspots.tsv" 2>&1
hotspots 0 "over the whole history" > "$OUT/04-hotspots-all-history.tsv" 2>&1
{
  echo "# file changes per top-level folder in the last $CHURN_DAYS days (where does change land?)"
  git log --since="@$since" --until="@$HEAD_TS" --name-only --pretty=format: HEAD \
    | grep -v '^$' | awk -F/ 'NF>1 {print $1"/"$2} NF==1 {print "(root)"}' | sort | uniq -c | sort -rn | head -20
  echo
  echo "# file changes per top-level folder over the whole history"
  git log --name-only --pretty=format: HEAD | grep -v '^$' | awk -F/ 'NF>1 {print $1"/"$2} NF==1 {print "(root)"}' | sort | uniq -c | sort -rn | head -12
  echo
  echo "# commits in the last $CHURN_DAYS days: $(git rev-list --count --since="@$since" --until="@$HEAD_TS" HEAD)   distinct authors: $(git log --since="@$since" --until="@$HEAD_TS" --format=%ae HEAD | sort -u | wc -l | tr -d ' ')"
  echo "# bot-authored commits (author contains 'bot'): $(git log --since="@$since" --until="@$HEAD_TS" --format=%an HEAD | grep -c -i bot)"
} > "$OUT/04-churn-by-folder.txt" 2>&1

# ---------------------------------------------------------------- 05 architecture
note "05 project references"
{
  echo "# project -> referenced projects (ProjectReference); fan-in = how many projects reference it"
  for p in $(git ls-files '*.csproj'); do
    refs=$(grep -o 'ProjectReference Include="[^"]*"' "$p" | sed -E 's/.*[\\/]([^\\/]+)\.csproj"/\1/' | paste -sd' ' -)
    printf '%-45s -> %s\n' "$(basename "$p" .csproj)" "${refs:-(none)}"
  done
  echo
  echo "# fan-in"
  git ls-files '*.csproj' | xa grep -h -o 'ProjectReference Include="[^"]*"' | sed -E 's/.*[\\/]([^\\/]+)\.csproj"/\1/' | sort | uniq -c | sort -rn
  echo
  echo "# source lines (non-blank, *.cs, excluding migrations/generated) per top-level project folder"
  cs_files | while read -r f; do printf '%s\t%s\n' "$(echo "$f" | cut -d/ -f1-2)" "$(grep -c -v '^\s*$' "$f")"; done \
    | awk -F'\t' '{s[$1]+=$2; n[$1]++} END {for (k in s) printf "%7d lines %4d files  %s\n", s[k], n[k], k}' | sort -rn
} > "$OUT/05-architecture.txt" 2>&1

# ---------------------------------------------------------------- 06 tests
note "06 tests (inventory; not run)"
{
  echo "# test projects: $(git ls-files '*.csproj' | grep -i -c test)"
  git ls-files '*.csproj' | grep -i test | sed 's/^/  /'
  echo
  echo "# test attributes"
  for a in '\[Fact' '\[Theory' '\[Test\]' '\[TestCase' '\[TestMethod' '\[DataTestMethod' '\[DataRow'; do
    printf '  %-18s %s\n' "$a" "$(cs_files | grep -i test | xa grep -E -o -- "$a" 2>/dev/null | wc -l | tr -d ' ')"
  done
  echo
  prod=$(cs_files | grep -v -i test | xa cat | grep -c -v '^\s*$')
  test=$(cs_files | grep -i test | xa cat | grep -c -v '^\s*$')
  echo "# non-blank C# lines: production $prod, test $test"
  echo
  echo "# test infrastructure in use"
  for t in Testcontainers WebApplicationFactory 'Aspire.Hosting.Testing' NSubstitute Moq FakeItEasy Playwright Verify; do
    printf '  %-26s %s files\n' "$t" "$(git ls-files '*.cs' '*.csproj' '*.props' '*.ts' | xa grep -l -- "$t" 2>/dev/null | wc -l | tr -d ' ')"
  done
  echo
  echo "# src projects without a same-named test project (heuristic: <Name>.UnitTests or <Name>.FunctionalTests)"
  for p in $(git ls-files 'src/*.csproj'); do n=$(basename "$p" .csproj); b=${n%.API};
    git ls-files '*.csproj' | grep -q -i -E "/($n|$b)\.(Unit|Functional|Integration)?Tests\.csproj$" || echo "  $n"; done
} > "$OUT/06-tests.txt" 2>&1

# ---------------------------------------------------------------- 07 security, observability, performance smells
note "07 code signals"
{
  echo "# Grep signals are LEADS, not findings. Open every hit before it goes in a report."
  echo "# Counts cover production code only (paths containing test/ or Tests/ are excluded)."
  echo
  echo "## Security (Ch 14)"
  count '\[AllowAnonymous\]'
  count 'RequireAuthorization\(|\[Authorize'
  count 'AllowAnyOrigin|SetIsOriginAllowed\(_ ?=> ?true'
  count 'RequireHttpsMetadata\s*=\s*false'
  count 'ValidateAudience\s*=\s*false|ValidateIssuer\s*=\s*false|ValidateLifetime\s*=\s*false'
  count 'UseDeveloperExceptionPage'
  count 'FromSqlRaw|ExecuteSqlRaw'
  count 'AddRateLimiter|RequireRateLimiting|\[EnableRateLimiting'
  count 'DangerousAcceptAnyServerCertificateValidator|ServerCertificateCustomValidationCallback'
  echo "  secret-looking literals in config/source (key names only):"
  git ls-files '*.json' '*.cs' '*.yml' '*.yaml' '*.env' | grep -v -E 'package(-lock)?\.json|node_modules' \
    | xa grep -n -i -E '"?(password|secret|apikey|api_key|connectionstring|clientsecret)"?\s*[:=]\s*"[^"$\{][^"]{3,}"' 2>/dev/null \
    | sed -E 's/([:=]\s*")[^"]*"/\1<redacted>"/' | head -20 | sed 's/^/    /'
  echo
  echo "## Observability (Ch 13)"
  count 'AddOpenTelemetry|WithTracing|WithMetrics'
  count 'AddHealthChecks|MapHealthChecks|MapDefaultEndpoints'
  count 'new ActivitySource|ActivitySource\('
  count 'new Meter\(|IMeterFactory'
  count 'Log(Trace|Debug|Information|Warning|Error|Critical)\(\$"'
  where 'Log(Trace|Debug|Information|Warning|Error|Critical)\(\$"'
  count '\[LoggerMessage'
  count 'catch\s*(\(Exception( \w+)?\))?\s*\{\s*\}'
  echo
  echo "## Performance and async smells (Ch 15, Ch 4, Ch 8)"
  count '\.Result\b|\.Wait\(\)|GetAwaiter\(\)\.GetResult\(\)'
  where '\.Result\b|\.Wait\(\)|GetAwaiter\(\)\.GetResult\(\)'
  count 'async void'
  where 'async void'
  count 'new HttpClient\('
  count '\.Include\('
  count 'AsNoTracking'
  count 'AsSplitQuery'
  count '\.ToList(Async)?\(\)\s*\.\s*(Where|Select|Count|Any|First)'
  count 'Task\.Run\('
  count 'CancellationToken'
  count 'Thread\.Sleep'
} > "$OUT/07-code-signals.txt" 2>&1

# ---------------------------------------------------------------- 08 build/CI/containers + cost drivers
note "08 build, CI and cost drivers"
{
  echo "## CI definitions"
  git ls-files '.github/workflows/*' 'azure-pipelines*.yml' '*.gitlab-ci.yml' 'ci.yml' 'Jenkinsfile' | sed 's/^/  /'
  echo
  echo "## What CI runs (run: lines)"
  for f in $(git ls-files '.github/workflows/*.yml' '.github/workflows/*.yaml'); do
    echo "  -- $f"; grep -E '^\s*(run:|- run:)|dotnet ' "$f" | sed 's/^\s*/    /'
  done
  echo
  echo "## Build hygiene (Directory.Build.props / csproj)"
  for s in TreatWarningsAsErrors Nullable ImplicitUsings AnalysisLevel EnforceCodeStyleInBuild UseArtifactsOutput Deterministic ContinuousIntegrationBuild; do
    printf '  %-26s %s\n' "$s" "$(git ls-files '*.props' '*.csproj' | xa grep -l "<$s>" 2>/dev/null | wc -l | tr -d ' ') files"
  done
  echo "  projects with <Nullable>enable: $(git ls-files '*.csproj' | xa grep -l '<Nullable>enable' | wc -l | tr -d ' ') of $(git ls-files '*.csproj' | wc -l | tr -d ' ') (plus any set in Directory.Build.props)"
  echo "  .editorconfig: $(git ls-files | grep -c -E '(^|/)\.editorconfig$')"
  echo
  echo "## Cost drivers: infrastructure the app provisions (Ch 28)"
  echo "  Aspire AppHost resources (Add* calls):"
  git ls-files '*AppHost*/*.cs' | xa grep -h -o -E '\.Add[A-Z][A-Za-z]+\(' 2>/dev/null | sort | uniq -c | sort -rn | sed 's/^/    /'
  echo "  IaC files: bicep $(git ls-files '*.bicep' | wc -l | tr -d ' '), terraform $(git ls-files '*.tf' | wc -l | tr -d ' '), helm/k8s yaml $(git ls-files | grep -c -E '(charts|k8s|kubernetes|deploy)/.*\.ya?ml$')"
  echo "  AI/LLM clients referenced:"
  git ls-files '*.cs' '*.csproj' '*.props' | xa grep -h -o -E '(AzureOpenAI|OpenAI|Ollama|Foundry)[A-Za-z]*' 2>/dev/null | sort | uniq -c | sort -rn | head -10 | sed 's/^/    /'
} > "$OUT/08-build-ci-cost.txt" 2>&1

# ---------------------------------------------------------------- 09 build + unit tests (opt-in)
if [ "$RUN_BUILD" = 1 ] && [ "$RUN_DOTNET" = 1 ] && have dotnet; then
  note "09 build and unit tests"
  sln="$SOLUTION"; [ -e "$sln" ] || sln="$(git ls-files '*.slnx' '*.sln' | head -1)"
  {
    echo "# dotnet build $sln (run from outside the repo, see 01)"
    start=$(date +%s)
    ( cd "$OUT" && dotnet build "$TARGET/$sln" -nologo 2>&1 ) | rel > "$OUT/09-build.log"
    echo "  wall time: $(( $(date +%s) - start )) s"
    grep -E '^\s*[0-9]+ (Warning|Error)\(s\)' "$OUT/09-build.log" | sed 's/^\s*/  /'
    echo "  distinct errors:"
    grep -E ': error ' "$OUT/09-build.log" | sed -E 's/ \[[^]]*\]$//' | sort -u | sed 's/^/    /'
    echo
    echo "# unit-test projects (functional tests need a container runtime and are not run here)"
    for p in $(git ls-files '*UnitTests.csproj'); do
      n=$(basename "$p" .csproj)
      if ( cd "$OUT" && dotnet build "$TARGET/$p" -nologo -v q > /dev/null 2>&1 ); then
        exe=$(find "$(dirname "$p")" "$TARGET/artifacts" -path "*/$n" -type f -perm -u+x 2>/dev/null | head -1)
        if [ -n "$exe" ]; then
          res=$("$exe" --no-progress 2>&1 | grep -E '^\s*(total|failed|succeeded|skipped|duration):' | xargs)
          echo "  $n: ran -> $res"
        else echo "  $n: built, but no test executable found (VSTest project? run dotnet test yourself)"; fi
      else echo "  $n: BUILD FAILED"; fi
    done
  } > "$OUT/09-build-tests.txt" 2>&1
fi

# ---------------------------------------------------------------- summary
note "summary"
{
  echo "# Health-check run — raw evidence index"
  echo
  sed 's/^/    /' "$OUT/00-environment.txt"
  echo
  echo "| File | Area | Chapter |"
  echo "|---|---|---|"
  echo "| 01-inventory-runtime.txt | projects, target frameworks, SDK pin, support status | Ch 30, App. B |"
  echo "| 02-dependencies-summary.txt (+ 02-packages-*.json) | outdated / vulnerable / deprecated, transitive included | Ch 35 |"
  echo "| 03-supply-chain.txt | sources, pinning, audit switches, action pins, base images | Ch 35 |"
  echo "| 04-hotspots.tsv, 04-hotspots-all-history.tsv, 04-churn-by-folder.txt | churn x complexity | Ch 30 |"
  echo "| 05-architecture.txt | project graph, fan-in, size | Ch 6 |"
  echo "| 06-tests.txt | test inventory (run them with --build) | Ch 7, Ch 25 |"
  echo "| 07-code-signals.txt | security, observability, performance leads | Ch 14, 13, 15, 4 |"
  echo "| 08-build-ci-cost.txt | CI, build hygiene, provisioned resources | Ch 11, 12, 28 |"
  [ -f "$OUT/09-build-tests.txt" ] && echo "| 09-build-tests.txt (+ 09-build.log) | does it build from a clean clone; unit tests | Ch 7, 12 |"
  echo
  echo "Skipped sections: $(grep -l SKIPPED "$OUT"/0*.txt 2>/dev/null | xa -n1 basename | paste -sd' ' - | sed 's/^$/none/')"
} > "$OUT/SUMMARY.md"

note "done: $OUT"
