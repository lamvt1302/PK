namespace PK.Server.Common;

public static class ApiError
{
    public static object Create(string code, string message, object? details = null)
        => new
        {
            error = new
            {
                code,
                message,
                details = details ?? new { }
            }
        };
}

