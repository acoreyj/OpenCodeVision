namespace OpenCodeSticky.Core;

public sealed record ServiceStatus(
    string Key,
    string DisplayName,
    Uri Url,
    string? Version,
    bool Enabled,
    bool Online);

public sealed class BlockedSessionEventArgs : EventArgs
{
    public BlockedSessionEventArgs(string serviceKey, string sessionId)
    {
        ServiceKey = serviceKey;
        SessionId = sessionId;
    }

    public string ServiceKey { get; }

    public string SessionId { get; }
}

public sealed record ServiceDescriptor(
    string Key,
    string DisplayName,
    Uri Url,
    string Password,
    string? Version = null,
    Uri? AlternateUrl = null);
