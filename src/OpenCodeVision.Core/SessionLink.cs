using System.Text;

namespace OpenCodeVision.Core;

/// <summary>
/// Builds the opencode web UI link for one session.
/// The web app routes sessions as <c>/server/{key}/session/{id}</c>, where the
/// key is the unpadded base64url form of the service URL.
/// </summary>
public static class SessionLink
{
    public static Uri For(Uri serviceUrl, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(serviceUrl);
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        var key = Key(serviceUrl);
        var path = $"/server/{key}/session/{Uri.EscapeDataString(sessionId)}";
        return new Uri(serviceUrl, path);
    }

    public static string Key(Uri serviceUrl)
    {
        ArgumentNullException.ThrowIfNull(serviceUrl);

        var url = serviceUrl.AbsoluteUri.TrimEnd('/');
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(url))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
