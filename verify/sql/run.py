#!/usr/bin/env python3
"""Runs Chapter 7's SQL querying examples on PostgreSQL and SQL Server and checks what the text says.

Called by verify.sh once both containers in docker-compose.yml are up. For every file in queries/
(printed in the chapter) and claims/ (behind a number or a statement in the prose) it:
  - runs the file on both engines and writes the raw output to reference-runs/<dir>-<name>.txt;
  - checks the engines agree (rows compared as a multiset), unless EXPECT below says otherwise;
and it checks that every file in queries/ is printed, character for character, in Chapter 7
in both editions.
"""
import os
import re
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
COMPOSE = ["docker", "compose", "-f", str(HERE / "docker-compose.yml")]
CHAPTERS = [ROOT / "chapters" / "107-data-access.md", ROOT / "chapters" / "uk" / "107-data-access.md"]

# How the two engines are expected to answer a file; anything not listed must give the same rows.
EXPECT = {
    "queries/08-recursive-cte.sql": "pg-only",         # SQL Server rejects WITH RECURSIVE
    "claims/recursive-cte-mssql.sql": "mssql-only",    # ...and accepts the plain WITH
    "claims/avg-int.sql": "differ",                    # AVG(int) is an int on SQL Server
    "claims/null-sort.sql": "differ-ordered",          # NULL sorts first on SQL Server, last on PostgreSQL
    "claims/window-in-where.sql": "error",             # both reject a window function in WHERE
    "claims/alias-in-where.sql": "error",              # both reject a SELECT alias in WHERE
    "claims/cte-plan-pg.sql": "pg-only",               # EXPLAIN is PostgreSQL's...
    "claims/cte-plan-mssql.sql": "mssql-only",         # ...SHOWPLAN_TEXT is SQL Server's
    "claims/semi-join-plan-pg.sql": "pg-only",
    "claims/semi-join-plan-mssql.sql": "mssql-only",
    "claims/nulls-last.sql": "pg-only",                # SQL Server has no NULLS FIRST / LAST
    "claims/recursion-limit-mssql.sql": "pg-error-only",  # PostgreSQL rejects the plain WITH; SQL Server stops at 100 levels
    "claims/recursion-limit-pg.sql": "pg-only",        # SQL Server rejects WITH RECURSIVE
}
# Plan text a statement in the prose rests on: (file, engine, regex, expected number of matches).
PLAN_CHECKS = [
    ("claims/cte-plan-pg.sql", "pg", r"CTE Scan", 2),           # only the CTE referenced twice: two scans of it
    ("claims/cte-plan-mssql.sql", "mssql", r"OBJECT:\(\[handbook\]\.\[dbo\]\.\[Orders\]", 2),  # Orders read twice
    ("claims/semi-join-plan-pg.sql", "pg", r"Hash Join\n  Hash Cond: \(c\.id = o\.customerid\)", 2),  # IN = EXISTS
    ("claims/semi-join-plan-pg.sql", "pg", r"Anti Join", 1),       # NOT EXISTS only
    ("claims/semi-join-plan-pg.sql", "pg", r"hashed SubPlan", 1),  # NOT IN: a per-row check
    ("claims/semi-join-plan-mssql.sql", "mssql", r"Left Semi Join", 2),
    ("claims/recursion-limit-mssql.sql", "mssql", r"Msg 530\b.*\n.*maximum recursion 100", 1),
    ("claims/recursion-limit-pg.sql", "pg", r"\n200\n", 1),
]
SAME_ROWS = [("queries/08-recursive-cte.sql", "pg", "claims/recursive-cte-mssql.sql", "mssql")]


def run_pg(sql: str):
    p = subprocess.run(COMPOSE + ["exec", "-T", "postgres", "psql", "-X", "-U", "postgres", "-A", "-F", "|",
                                  "-P", "null=NULL", "-v", "ON_ERROR_STOP=1", "-f", "-"],
                       input=sql, capture_output=True, text=True)
    return p.returncode, p.stdout + p.stderr


def run_mssql(sql: str, db: str = "handbook"):
    p = subprocess.run(COMPOSE + ["exec", "-T", "mssql", "/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost",
                                  "-U", "sa", "-P", "Local-Only-Passw0rd!", "-C", "-b", "-d", db,
                                  "-W", "-s", "|", "-i", "/dev/stdin"],
                       input=sql, capture_output=True, text=True)
    return p.returncode, p.stdout + p.stderr


def normalize(out: str, ordered: bool) -> list:
    """Result sets as lists of rows: header lower-cased, separator lines dropped, row-count footers unified."""
    sets, cur = [], []
    for line in out.splitlines():
        line = line.strip()
        if not line or re.fullmatch(r"[-|]+", line):
            continue
        m = re.fullmatch(r"\((\d+) rows?(?: affected)?\)", line)
        if m:
            header, rows = (cur[0].lower(), cur[1:]) if cur else ("", [])
            sets.append([header] + (rows if ordered else sorted(rows)) + [f"({m.group(1)} rows)"])
            cur = []
        else:
            cur.append(line)
    return sets


def main() -> int:
    failures = []
    rc, out = run_mssql("IF DB_ID('handbook') IS NULL CREATE DATABASE handbook;", db="master")
    if rc:
        print(out); return 1
    schema = (HERE / "schema.sql").read_text()
    for name, (rc, out) in (("PostgreSQL", run_pg(schema)), ("SQL Server", run_mssql("SET NOCOUNT ON;\n" + schema))):
        if rc:
            print(f"schema failed on {name}:\n{out}"); return 1

    env = os.environ.get("SQL_ENV_HEADER", "")
    results = {}
    files = sorted(HERE.glob("queries/*.sql")) + sorted(HERE.glob("claims/*.sql"))
    for f in files:
        rel = f"{f.parent.name}/{f.name}"
        sql = f.read_text()
        pg = run_pg(sql)
        ms = run_mssql(sql)
        results[(rel, "pg")], results[(rel, "mssql")] = pg, ms
        (HERE / "reference-runs" / f"{f.parent.name}-{f.stem}.txt").write_text(
            f"{env}\n# File: verify/sql/{rel}\n\n-- PostgreSQL (exit code {pg[0]})\n{pg[1]}\n"
            f"-- SQL Server (exit code {ms[0]})\n{ms[1]}")
        mode = EXPECT.get(rel, "same")
        ok = {
            "same": pg[0] == 0 and ms[0] == 0 and normalize(pg[1], False) == normalize(ms[1], False),
            "differ": pg[0] == 0 and ms[0] == 0 and normalize(pg[1], False) != normalize(ms[1], False),
            "differ-ordered": pg[0] == 0 and ms[0] == 0 and normalize(pg[1], False) == normalize(ms[1], False)
                              and normalize(pg[1], True) != normalize(ms[1], True),
            "error": pg[0] != 0 and ms[0] != 0,
            "pg-only": pg[0] == 0 and ms[0] != 0,
            "mssql-only": pg[0] != 0 and ms[0] == 0,
            "pg-error-only": pg[0] != 0 and ms[0] != 0,
        }[mode]
        print(f"{'ok  ' if ok else 'FAIL'} {rel} ({mode})")
        if not ok:
            failures.append(rel)

    for a, ea, b, eb in SAME_ROWS:
        if normalize(results[(a, ea)][1], False) != normalize(results[(b, eb)][1], False):
            failures.append(f"{a} on {ea} vs {b} on {eb}")
            print(f"FAIL {a} ({ea}) and {b} ({eb}) return different rows")

    for rel, engine, pattern, count in PLAN_CHECKS:
        found = len(re.findall(pattern, results[(rel, engine)][1]))
        if found != count:
            failures.append(f"{rel}: {pattern}")
            print(f"FAIL {rel}: expected {count} x {pattern!r} in the {engine} plan, found {found}")

    texts = {c: c.read_text() for c in CHAPTERS}
    for f in sorted(HERE.glob("queries/*.sql")):
        block = "```sql\n" + f.read_text()
        for c, text in texts.items():
            if block + "```" not in text:
                failures.append(f"{f.name} not printed in {c.relative_to(ROOT)}")
                print(f"FAIL {f.name} is not printed verbatim in {c.relative_to(ROOT)}")

    print(f"\n{len(files)} files on both engines, {len(failures)} failure(s)")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
