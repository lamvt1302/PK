using Microsoft.AspNetCore.Mvc;
using PK.Server.Common;
using PK.Server.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace PK.Server.Controllers;

[ApiExplorerSettings(IgnoreApi = false)]
[Route("internal/economy")]
public class InternalEconomyController : BaseApiController
{
    private readonly EconomyService _economy;

    public InternalEconomyController(EconomyService economy)
    {
        _economy = economy;
    }

    public sealed record TxRequest(
        Guid player_id,
        Guid request_id,
        string type,
        long amount,
        string reason
    );

    [HttpPost("transactions")]
    [SwaggerOperation(Summary = "Internal: apply economy transaction (dev only)")]
    public async Task<IActionResult> Apply([FromBody] TxRequest req)
    {
        // Bảo vệ tối thiểu: endpoint này yêu cầu X-Internal-Key (được check ở InternalAuthMiddleware).
        var result = await _economy.ApplyTransaction(req.player_id, req.request_id, req.type, req.amount, req.reason);
        if (!result.Success)
        {
            var code = result.ErrorCode ?? "INTERNAL";
            var status = code is "INSUFFICIENT_FUNDS" or "NO_SPINS" or "IDEMPOTENCY_KEY_CONFLICT" ? 409 : 400;
            return StatusCode(status, ApiError.Create(code, result.ErrorMessage ?? "Có lỗi xíu, thử lại nha!", result.ErrorDetails ?? new { }));
        }

        var log = result.Log!;
        return Ok(new
        {
            applied = result.WasApplied,
            idempotency_replayed = result.WasReplayed,
            before = new Dictionary<string, object?> { [log.Currency] = log.BeforeValue },
            after = new Dictionary<string, object?> { [log.Currency] = log.AfterValue }
        });
    }
}
