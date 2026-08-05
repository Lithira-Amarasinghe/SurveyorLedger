using SurveyorLedger.Core.Exceptions;
using SurveyorLedger.API.Models.Responses;

namespace SurveyorLedger.API.Middleware;

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var response = exception switch
        {
            AppException ex => (ex.StatusCode, ApiResponse<object>.Fail(ex.Message)),
            _ => (500, ApiResponse<object>.Fail("Internal server error"))
        };

        context.Response.StatusCode = response.Item1;
        return context.Response.WriteAsJsonAsync(response.Item2);
    }
}
