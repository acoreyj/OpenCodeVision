namespace OpenCodeSticky.Core.Discovery;

public sealed record ServiceCandidate(
    string Key,
    string DisplayName,
    Uri Url,
    string Password,
    Uri? AlternateUrl = null);

public interface IWslGateway
{
    Task<IReadOnlyList<string>> ListDistrosAsync(CancellationToken cancellationToken);

    Task<string?> ReadServiceFileAsync(string distro, CancellationToken cancellationToken);

    Task<string?> GetDistroAddressAsync(string distro, CancellationToken cancellationToken);
}
