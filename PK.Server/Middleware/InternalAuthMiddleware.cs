using PK.Server.Common;

namespace PK.Server.Middleware;

/// <summary>
/// Chặn các endpoint internal (vd: /internal/economy/*) bằng shared key.
/// Đây là lớp bảo vệ tối thiểu để tránh "public economy".
/// </summary>
public class InternalAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _config;

    public InternalAuthMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _config = config;
    }

    public async Task Invoke(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/internal"))
        {
            await _next(context);
            return;
        }

        var expected = _config["Internal:Key"];
        var provided = context.Request.Headers["X-Internal-Key"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(expected) || provided != expected)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(ApiError.Create("UNAUTHORIZED", "Chưa đăng nhập"));
            return;
        }

        await _next(context);
    }
}

