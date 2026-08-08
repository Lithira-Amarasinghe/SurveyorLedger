---
description: Scaffold a new Angular standalone page component following this repo's Phase 2 UI spec
---

Scaffold UI page for: $ARGUMENTS

Steps:
1. Check [UI_IMPLEMENTATION_GUIDE.md](../../UI_IMPLEMENTATION_GUIDE.md) for the spec of this page before writing anything.
2. Create standalone component under `ui/src/app/pages/<area>` — no NgModule.
3. Use signals (`InputSignal`, `computed`) for local state, not RxJS subjects unless the guide calls for a stream.
4. Wire route in the relevant routing config; add guard if the page requires auth/role.
5. Use existing `core/services` for API calls — don't hand-roll HTTP calls in the component.
6. Style with Tailwind utility classes + Material components per existing pages, don't introduce a new UI kit.
7. Reuse `shell/` (Sidebar, Topbar, CommandPalette) layout wiring already established — don't duplicate shell logic per page.

After scaffolding, start the UI dev server and check the page renders before calling it done.
