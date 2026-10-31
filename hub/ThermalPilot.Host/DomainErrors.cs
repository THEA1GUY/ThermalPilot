namespace ThermalPilot.Host;

public sealed class RequestFailure(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed class StorageFailure(string code, Exception inner) : Exception("Local storage is unavailable.", inner)
{
    public string Code { get; } = code;
}
