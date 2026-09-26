# Health-check run — raw evidence index

    date        : 2026-09-26 22:12 UTC
    machine     : 4 vCPU (Intel(R) Xeon(R) Processor @ 2.10GHz), 16 GB RAM
    os          : Ubuntu 24.04.4 LTS
    git         : 2.43.0
    dotnet sdks : 10.0.112
    target      : https://github.com/dotnet/eShop.git
    commit      : b4a40872005d4bb29e5b1fa1ff7e244143d39215 (2026-08-28)
    history     : 347 commits since 2023-10-18

| File | Area | Chapter |
|---|---|---|
| 01-inventory-runtime.txt | projects, target frameworks, SDK pin, support status | Ch 30, App. B |
| 02-dependencies-summary.txt (+ 02-packages-*.json) | outdated / vulnerable / deprecated, transitive included | Ch 35 |
| 03-supply-chain.txt | sources, pinning, audit switches, action pins, base images | Ch 35 |
| 04-hotspots.tsv, 04-hotspots-all-history.tsv, 04-churn-by-folder.txt | churn x complexity | Ch 30 |
| 05-architecture.txt | project graph, fan-in, size | Ch 6 |
| 06-tests.txt | test inventory (run them with --build) | Ch 7, Ch 25 |
| 07-code-signals.txt | security, observability, performance leads | Ch 14, 13, 15, 4 |
| 08-build-ci-cost.txt | CI, build hygiene, provisioned resources | Ch 11, 12, 28 |
| 09-build-tests.txt (+ 09-build.log) | does it build from a clean clone; unit tests | Ch 7, 12 |

Skipped sections: none
