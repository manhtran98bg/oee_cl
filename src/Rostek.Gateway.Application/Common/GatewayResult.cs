namespace Rostek.Gateway.Application.Common;

public sealed record GatewayResult(bool Succeeded, string? ErrorMessage = null)
{
    public static GatewayResult Ok() => new(true);
    public static GatewayResult Fail(string message) => new(false, message);
}

public sealed record GatewayResult<T>(bool Succeeded, T? Value, string? ErrorMessage = null)
{
    public static GatewayResult<T> Ok(T value) => new(true, value);
    public static GatewayResult<T> Fail(string message) => new(false, default, message);
}
