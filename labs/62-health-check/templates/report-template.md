<!-- The full report. Page 1 is executive-summary.md. Everything after page 1 is for the engineers and the person who will check your work. -->

**[System name]: .NET health check**
[Client] · [date] · version [1.0] · prepared by [you] · for [sponsor, role]

**1. Executive summary** (one page: paste executive-summary.md)

**2. Scope and method**
- **Question the client asked:** [their words]
- **What we assessed:** [repos, commit SHAs, environments, dates]
- **What we did not assess:** [production data, infrastructure accounts, load, ...]
- **How:** automated checks (`health-check.sh`, evidence folder `[path]`), code reading, [n] interviews ([roles]), [dashboards/pipelines seen]
- **Tool versions and environment:** [SDK, OS, date], see the evidence header

**3. Context: what "healthy" means here**
[Two or three sentences on the business goal and constraints that the scores are measured against, e.g. "a B2C shop expecting [n]x traffic at [event], a two-person team, card data kept out of scope by a hosted payment page".]

**4. Risk matrix**

```text
                 impact →  1 negligible  2 minor  3 moderate  4 major  5 severe
likelihood
5 happening
4 likely
3 possible
2 unlikely
1 rare
```
Place finding IDs in the cells. The top-right corner is the executive summary.

**5. Findings by area**
For each area: a one-line verdict (good / watch / act), then the finding cards (templates/finding-card.md), highest risk first.

- **5.1 Runtime and end of support**
- **5.2 Dependencies**
- **5.3 Supply chain**
- **5.4 Change hotspots**
- **5.5 Architecture and coupling**
- **5.6 Tests**
- **5.7 Security posture**
- **5.8 Observability**
- **5.9 Performance**
- **5.10 Build, CI and containers**
- **5.11 Cost drivers**

**6. Roadmap**

| When | Actions (finding IDs) | Effort | Outcome you can measure |
|---|---|---|---|
| First two weeks | | | |
| Next quarter | | | |
| Later / only if [trigger] | | | |

**7. Strengths to keep**
[What the team did well and should protect while changing things. Specific, with evidence.]

**Appendix A: all findings** (one line each: ID, title, area, L x I, effort, status)
**Appendix B: evidence index** (file -> what it shows)
**Appendix C: open questions** (things to confirm with the team)
