#!/usr/bin/env python3
"""Condense sqlcmd output (tab-separated, -W) into something readable:
   * SET STATISTICS PROFILE rows -> actual rows | executes | estimated rows | operator tree
   * SET STATISTICS IO lines     -> table, scan count, logical reads
   * query result sets           -> header plus the first three rows and a row count."""
import sys

lines = sys.stdin.read().splitlines()
i = 0
while i < len(lines):
    line = lines[i]
    cols = line.split("\t")
    next_is_rule = i + 1 < len(lines) and set(lines[i + 1].replace("\t", "")) <= {"-"} and lines[i + 1].strip()
    if cols[:3] == ["Rows", "Executes", "StmtText"]:
        est = cols.index("EstimateRows")
        print(f"{'actual':>9} {'execs':>6} {'estimated':>10}  operator")
        i += 2
        while i < len(lines) and lines[i].count("\t") >= est:
            c = lines[i].split("\t")
            text = c[2].replace("[shop].[dbo].", "").rstrip()
            print(f"{c[0]:>9} {c[1]:>6} {c[est]:>10}  {text}")
            i += 1
        continue
    if next_is_rule:  # a result set
        print(line.replace("\t", " | "))
        i += 2
        rows = []
        while i < len(lines) and lines[i] and not lines[i].startswith(("Table '", "===", "Rows\tExecutes")):
            starts_next_set = i + 1 < len(lines) and lines[i + 1].strip() and set(lines[i + 1].replace("\t", "")) <= {"-"}
            if starts_next_set:
                break
            rows.append(lines[i]); i += 1
        for r in rows[:3]:
            print(r.replace("\t", " | "))
        if len(rows) > 3:
            print(f"... ({len(rows):,} rows)")
        continue
    if line.startswith("Table '"):
        parts = [p.strip() for p in line.rstrip(".").split(",")]
        keep = [p for p in parts if p.startswith(("Table", "Scan count", "logical reads"))]
        print(", ".join(keep).replace("'. Scan", "': scan"))
    elif line.strip() and not line.startswith("Changed database context"):
        print(line)
    i += 1
