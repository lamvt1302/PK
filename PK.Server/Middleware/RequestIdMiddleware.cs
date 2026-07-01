namespace PK.Server.Middleware;

public class RequestIdMiddleware
{
    private readonly RequestDelegate _next;

    public RequestIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        const string header = "X-Request-Id";

        // Header lookup is case-insensitive in ASP.NET Core, but iterate explicitly
        // so that "x-request-id", "X-REQUEST-ID", etc. are all accepted consistently.
        string? requestId = null;
        foreach (var key in context.Request.Headers.Keys)
        {
            if (string.Equals(key, header, StringComparison.OrdinalIgnoreCase))
            {
                requestId = context.Request.Headers[key].FirstOrDefault();
                break;
            }
        }

        var method = context.Request.Method;
        var isWrite =
            HttpMethods.IsPost(method) ||
            HttpMethods.IsPut(method) ||
            HttpMethods.IsPatch(method) ||
            HttpMethods.IsDelete(method);

        // Quy tắc theo spec:
        // - Request thay đổi state/currency (POST/PUT/PATCH/DELETE) bắt buộc phải có X-Request-Id.
        // - GET/HEAD: cho phép thiếu request id (server tự generate để trace).
        if (string.IsNullOrWhiteSpace(requestId))
        {
            if (isWrite)
            {
                // Exception: guest-login không bắt buộc X-Request-Id trong sprint specs,
                // vì idempotency chính là theo device_id.
                // Internal economy cũng dùng request_id trong body.
                if (context.Request.Path.StartsWithSegments("/api/v1/player/guest-login")
                    || context.Request.Path.StartsWithSegments("/internal/economy/transactions")
                    || context.Request.Path.StartsWithSegments("/api/v1/spin/daily-reward"))
                {
                    requestId = Guid.NewGuid().ToString();
                }
                else
                {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(PK.Server.Common.ApiError.Create(
                    "MISSING_REQUEST_ID",
                    "Thiếu yêu cầu ID"
                ));
                return;
                }
            }

            requestId = Guid.NewGuid().ToString();
        }

        context.Response.Headers[header] = requestId;
        context.Items[header] = requestId;

        await _next(context);
    }
}
