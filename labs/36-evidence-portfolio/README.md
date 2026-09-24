# Lab kit — Chapter 36: The Story Bank & Evidence Portfolio

Templates, prompts and one script for turning what you have done into claims you can prove. Read Chapter 36 first; this folder is the toolbox it refers to.

> **The portfolio rule.** Nothing you write for this lab goes into *this* repository. Stories about real employers go into a **private** story bank (a private repo or a notes folder). Lab artifacts and your evidence index go into **your own public portfolio repo**.

## What's here

| Path | Use it for | Goes into |
|---|---|---|
| `scripts/mine-git.sh` | Listing story candidates from your own commit history | Private story bank |
| `templates/star-worksheet.md` | One file per story | Private story bank |
| `templates/coverage-matrix.md` | Checking your stories cover the questions you will be asked | Private story bank |
| `templates/brag-doc-weekly.md` | Fifteen minutes every Friday | Private story bank |
| `templates/cv-bullets.md` | Problem → decision → measured result, plus the honesty checklist | Private drafts → public CV |
| `templates/portfolio-README.md` | The evidence index at the top of your public portfolio repo | Public portfolio |
| `prompts/behavioral-interviewer.md` | A mock behavioral interview with follow-ups and a scored debrief | — |
| `prompts/story-drilldown.md` | Finding the holes in a written story before you rehearse it | — |
| `prompts/cv-claim-deep-dive.md` | Stress-testing one CV bullet | — |
| `prompts/bar-raiser.md` | Hostile follow-ups, for stories that already hold up | — |
| `prompts/debrief-scorer.md` | Scoring a transcript — and calibrating the scorer first | — |
| `prompts/system-design-interviewer.md` | Timed design reps (used again by the system-design lab) | — |

## Quick start

```bash
# 1. Create your two repositories (names are suggestions).
mkdir -p ~/story-bank/stories ~/story-bank/brag ~/story-bank/mocks
gh repo create my-dotnet-portfolio --public --clone  # or use the GitHub UI

# 2. Copy the templates you need.
cp templates/star-worksheet.md templates/coverage-matrix.md ~/story-bank/
cp templates/portfolio-README.md ~/my-dotnet-portfolio/README.md

# 3. Mine a year of your own commits for story candidates.
./scripts/mine-git.sh --since "12 months ago" ~/src/work-repo-1 ~/src/work-repo-2 \
    > ~/story-bank/candidates.md
```

`mine-git.sh` needs only `bash` and `git`. By default it uses each repo's `git config user.email` as the author; pass `--author "Your Name"` if your commits used a different email.

## Before you paste anything into an AI assistant

Remove customer names, internal system names, hostnames and figures your employer has not published, and check your employer's policy on external AI tools. The prompts work just as well with "a payments service at a mid-size retailer" as with the real name.

## Last verified

2026-09-24 — `mine-git.sh` run with bash 5.2, mawk 1.3.4 and git 2.43 on Ubuntu 24.04 against a 124-commit fixture repository (reverts, weekend and late-night commits, keyword false positives) and passes ShellCheck 0.11. It is written for bash 3.2 and BSD awk (macOS) but was not run on macOS. The prompts were not run against a model during verification.
