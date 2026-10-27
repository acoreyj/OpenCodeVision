namespace OpenCodeSticky.Core.Discovery;

public sealed class ServiceDiscovery
{
    private readonly IWslGateway _wsl;
    private readonly string _nativeStatePath;

    public ServiceDiscovery(IWslGateway wsl, string nativeStatePath)
    {
        _wsl = wsl;
        _nativeStatePath = nativeStatePath;
    }

    public async Task<IReadOnlyList<ServiceCandidate>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var candidates = new List<ServiceCandidate>();

        var native = ReadNative();
        if (native is not null)
        {
            candidates.Add(new ServiceCandidate("native", "Windows", native.Url, native.Password));
        }

        IReadOnlyList<string> distros;
        try
        {
            distros = await _wsl.ListDistrosAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            distros = Array.Empty<string>();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in distros)
        {
            var distro = raw.Trim();
            if (distro.Length == 0 || !seen.Add(distro))
            {
                continue;
            }

            try
            {
                var text = await _wsl.ReadServiceFileAsync(distro, cancellationToken);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var file = ServiceFileParser.Parse(text);
                var alternate = await ReadAlternateAsync(distro, file.Url, cancellationToken);
                candidates.Add(new ServiceCandidate($"wsl:{distro}", distro, file.Url, file.Password, alternate));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // One broken distro must not hide the rest.
            }
        }

        return candidates;
    }

    private ServiceFile? ReadNative()
    {
        try
        {
            return File.Exists(_nativeStatePath)
                ? ServiceFileParser.Parse(File.ReadAllText(_nativeStatePath))
                : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<Uri?> ReadAlternateAsync(string distro, Uri serviceUrl, CancellationToken cancellationToken)
    {
        try
        {
            var address = await _wsl.GetDistroAddressAsync(distro, cancellationToken);
            if (string.IsNullOrWhiteSpace(address))
            {
                return null;
            }

            var host = address.Trim();
            if (host.Contains(':'))
            {
                host = $"[{host}]";
            }

            var port = serviceUrl.IsDefaultPort ? 80 : serviceUrl.Port;
            return new Uri($"http://{host}:{port}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
