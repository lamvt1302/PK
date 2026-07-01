using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace PK.Server.Services;

public class AuthService
{
    private readonly IDistributedCache _cache;

    public AuthService(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<string> IssueGuestToken(Guid playerId, TimeSpan? ttl = null)
    {
        ttl ??= TimeSpan.FromDays(7);
        var token = Guid.NewGuid().ToString("N");
        var key = TokenKey(token);
        await _cache.SetStringAsync(
            key,
            JsonSerializer.Serialize(new TokenRecord(playerId)),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }
        );
        return token;
    }

    public async Task<Guid?> TryGetPlayerIdByToken(string token)
    {
        var json = await _cache.GetStringAsync(TokenKey(token));
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var record = JsonSerializer.Deserialize<TokenRecord>(json);
            return record?.PlayerId;
        }
        catch
        {
            return null;
        }
    }

    private static string TokenKey(string token) => $"session:{token}";

    private sealed record TokenRecord(Guid PlayerId);
}

