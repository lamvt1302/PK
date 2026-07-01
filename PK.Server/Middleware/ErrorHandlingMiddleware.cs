using PK.Server.Common;
using PK.Server.Services;

namespace PK.Server.Middleware;

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (EconomyRuleException ex)
        {
            // Các lỗi nghiệp vụ nên trả format chuẩn.
            context.Response.StatusCode = ex.Code switch
            {
                "INSUFFICIENT_FUNDS" => StatusCodes.Status409Conflict,
                "NO_SPINS" => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            };
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(ApiError.Create(ex.Code, ex.MessageSafe, ex.Details));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(ApiError.Create("INTERNAL", "Lỗi server, thử lại nha!"));
        }
    }
}

