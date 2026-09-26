<!-- One card per finding. Keep the raw list (all cards) in an appendix; only the top 5 reach the executive summary. -->

**Finding [ID]: [a title that states the consequence, not the technology]**
e.g. "Security alerts for known-vulnerable packages are switched off", not "NoWarn NU1901-1904"

**Area** [runtime | dependencies | supply chain | hotspots | architecture | tests | security | observability | performance | build/CI | cost]

**Observation**
What is true, in one or two sentences. No adjectives, no blame, no "unfortunately".

**Evidence**
Where anyone can check it: file and line at commit [sha], command and its output (file in the evidence folder), dashboard, or interview note with date.
- `[path:line]`
- `[command]` -> `[evidence file]`

**Business impact**
What happens to the business if this stays as it is: revenue, customers, compliance, delivery speed, cost, key-person risk. Name the scenario.

**Likelihood** [1 rare · 2 unlikely · 3 possible · 4 likely · 5 already happening]
Why this score, in one line.

**Impact** [1 negligible · 2 minor · 3 moderate · 4 major · 5 severe]
Why this score, in one line, in the client's terms.

**Risk** likelihood x impact = [n]   **Confidence** [high | medium | low: what would raise it]

**Recommendation**
One action, starting with a verb. If there are options, the one you recommend first.

**Effort** [S under 1 day | M 1-5 days | L 1-4 weeks | XL more: split it]   **Owner** [role, not a name]

**If we do nothing**
The date or trigger when this gets worse (end of support, audit, next peak, next hire).
