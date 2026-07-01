using PK.Server.Common;

namespace PK.Server.Middleware;

/// <summary>
/// Chuẩn hoá response 429 theo error format chung.
/// </summary>
public class RateLimitErrorMiddleware
{
    private readonly RequestDelegate _next;

    public RateLimitErrorMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        await _next(context);

        if (context.Response.StatusCode == StatusCodes.Status429TooManyRequests
            && !context.Response.HasStarted
            && (context.Response.ContentType == null || !context.Response.ContentType.Contains("application/json")))
        {
            context.Response.ContentType = "application/json";
            // Bug #1: friendly Vietnamese message so the client can show a clear
            // "spin too fast" hint instead of an empty/generic 429 body.
            await context.Response.WriteAsJsonAsync(ApiError.Create(
                "RATE_LIMITED",
                "Quay nhanh quá, chờ xíu rồi bấm lại nha!"));
        }
    }
}

