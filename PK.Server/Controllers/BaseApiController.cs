using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using PK.Server.Common;

namespace PK.Server.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class BaseApiController : ControllerBase
{
    protected Guid? TryGetPlayerId()
    {
        if (HttpContext.Items.TryGetValue("player_id", out var v) && v is Guid g) return g;
        return null;
    }

    protected Guid? TryGetRequestId()
    {
        var s = HttpContext.Items["X-Request-Id"] as string;
        if (Guid.TryParse(s, out var g)) return g;
        return null;
    }

    /// <summary>
    /// Returns the raw X-Request-Id string (case-insensitive lookup) regardless of
    /// whether it is a valid UUID. Used to distinguish a missing header from an
    /// invalid (non-UUID) header so the caller can return the right error code.
    /// </summary>
    protected string? TryGetRawRequestId()
    {
        // Header lookup is case-insensitive in ASP.NET Core, but be explicit.
        var s = HttpContext.Items["X-Request-Id"] as string;
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    /// <summary>
    /// Common helper for write endpoints: returns the request id Guid, or a
    /// BadRequest with MISSING_REQUEST_ID (header absent). Any non-empty
    /// header value is accepted: UUIDs are parsed directly, and any other
    /// non-empty string is hashed to a deterministic Guid so it can be used
    /// consistently for idempotency.
    /// </summary>
    protected IActionResult? TryRequireRequestId(out Guid requestId)
    {
        var raw = TryGetRawRequestId();
        if (raw == null)
        {
            requestId = Guid.Empty;
            return BadRequest(ApiError.Create("MISSING_REQUEST_ID", "Thiếu yêu cầu ID"));
        }

        if (Guid.TryParse(raw, out requestId))
        {
            return null;
        }

        // Non-UUID string: hash to a deterministic Guid so any string becomes
        // a valid request id for idempotency purposes.
        requestId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(raw)).Take(16).ToArray());
        return null;
    }

    protected IActionResult UnauthorizedError()
        => Unauthorized(ApiError.Create("UNAUTHORIZED", "Chưa đăng nhập"));
}

