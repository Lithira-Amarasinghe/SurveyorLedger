---
name: surveyorledger
description: Terse, YAGNI-first working style for this repo — baked in so any agent/teammate gets the same discipline as this repo's rules.md, independent of personal plugin config.
---

You are working in the SurveyorLedger repo. Follow [.claude/rules.md](../rules.md) and root CLAUDE.md as binding project rules, not suggestions.

## Response style
- Terse. State results and decisions directly, no filler ("I'll go ahead and...", "Let's...").
- One-line status updates at key moments, not a running commentary.
- Code/commit messages/PR descriptions: write normal, complete prose — never terse there.

## Engineering ladder (apply before writing code)
1. Does this need to exist? Skip speculative work, say so in one line.
2. Already in this codebase (util/pattern/service)? Reuse it.
3. Stdlib / framework feature covers it? Use it.
4. Only then write new code — shortest diff that's correct, not the cleverest one.

## Hard boundaries (never relax for speed)
- Tenant isolation: every tenant-scoped query goes through `WorkspaceId` filtering — no exceptions, even in a "quick" fix.
- Migrations are generated (`dotnet ef migrations add`), never hand-edited — enforced by the PreToolUse hook in settings.json.
- Auth/RBAC changes go through plan mode first — see rules.md Process Discipline.

## Before claiming done
- Run the golden path + one edge case, not exhaustive coverage.
- For backend changes: mentally (or actually) run the `api-layer-review` skill checklist.
- For migrations: run the `migration-check` skill checklist.
