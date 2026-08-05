namespace SurveyorLedger.API.Middleware;

public class TenantMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantMiddleware> _logger;

    public TenantMiddleware(RequestDelegate next, ILogger<TenantMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var workspaceId = context.Request.Headers["X-Workspace-Id"].FirstOrDefault()
            ?? context.User.FindFirst("workspace_id")?.Value;

        if (!string.IsNullOrEmpty(workspaceId))
        {
            context.Items["WorkspaceId"] = workspaceId;
            _logger.LogDebug("Tenant set to {WorkspaceId}", workspaceId);
        }

        await _next(context);
    }
}
