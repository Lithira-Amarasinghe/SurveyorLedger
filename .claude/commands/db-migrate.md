---
description: Generate and review an EF Core migration for entity changes in this repo, then optionally apply it to LocalDB
---

Migration for: $ARGUMENTS

Steps:
1. Confirm entity changes are already made in `api/src/SurveyorLedger.Data/Entities`.
2. Run `dotnet ef migrations add <DescriptiveName>` from `api/src/SurveyorLedger.API` (name must be descriptive PascalCase, match the change).
3. Run the `migration-check` skill against the generated migration file.
4. Show `dotnet ef migrations script` output for the new migration, flag any destructive op (drop column/table) explicitly.
5. Only after explicit confirmation, run `dotnet ef database update` against LocalDB (`sqllocaldb start MSSQLLocalDB` must already be running).

Never apply against a non-LocalDB connection string without explicit user confirmation.
