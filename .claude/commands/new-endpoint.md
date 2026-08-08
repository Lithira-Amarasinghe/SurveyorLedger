---
description: Scaffold a new API endpoint (controller action + service method + DTOs) following this repo's Clean layers pattern
---

Scaffold endpoint for: $ARGUMENTS

Steps:
1. Identify resource/controller — reuse existing controller under `api/src/SurveyorLedger.API/Controllers` if one fits the resource, else create new one.
2. Add service method to matching service in `api/src/SurveyorLedger.API/Services` (or `SurveyorLedger.Core` if shared) — business logic lives here, not in the controller.
3. Add request/response DTOs in the API project, entities stay in `SurveyorLedger.Data`.
4. Wire tenant isolation: pull `WorkspaceId` from `TenantMiddleware` context, scope every query by it.
5. Apply Casbin policy / `[Authorize]` for the correct roles (Admin/Manager/Surveyor/Client).
6. Throw typed exceptions (inherit `AppException`) for error cases, let middleware format the response.
7. Add unit test for the service method, integration test for the endpoint (Testcontainers.MsSql, no mocked DB).

After scaffolding, run the `api-layer-review` skill against the changed files before considering it done.
