using System.Security.Cryptography;
using System.Text;

namespace PK.Server.Common;

public static class DeterministicGuid
{
    public static Guid From(Guid baseId, string purpose)
    {
        // Tạo GUID ổn định từ (baseId + purpose) để:
        // - 1 request id (X-Request-Id) có thể sinh ra nhiều sub-request-id
        // - vẫn đảm bảo retry tạo ra cùng các sub-id => idempotent end-to-end.
        var input = $"{baseId:D}:{purpose}";
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);
        return new Guid(guidBytes);
    }
}

