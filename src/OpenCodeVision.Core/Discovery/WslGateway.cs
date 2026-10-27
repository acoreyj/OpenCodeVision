using System.Diagnostics;
using System.Text;

namespace OpenCodeVision.Core.Discovery;

public sealed class WslGateway : IWslGateway
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static IReadOnlyList<string> DecodeDistroList(byte[] bytes)
    {
        var text = Encoding.Unicode.GetString(bytes);
        return text
            .TrimStart('\uFEFF')
            .Split('\n')
            .Select(line => line.Trim('\r', ' ', '\0', '\uFEFF'))
            .Where(line => line.Length > 0)
            .ToList();
    }

    public async Task<IReadOnlyList<string>> ListDistrosAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunAsync(new[] { "-l", "-q" }, cancellationToken);
            return result.ExitCode == 0
                ? DecodeDistroList(result.StdOut)
                : Array.Empty<string>();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Array.Empty<string>();
        }
        catch
        {
            // A missing or broken wsl.exe means no WSL candidates.
            return Array.Empty<string>();
        }
    }

    public async Task<string?> ReadServiceFileAsync(string distro, CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunAsync(
                new[] { "-d", distro, "-e", "sh", "-lc", "cat \"$HOME/.local/state/opencode/service.json\"" },
                cancellationToken);
            return result.ExitCode == 0 ? Encoding.UTF8.GetString(result.StdOut) : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> GetDistroAddressAsync(string distro, CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunAsync(new[] { "-d", distro, "-e", "hostname", "-I" }, cancellationToken);
            if (result.ExitCode != 0)
            {
                return null;
            }

            return Encoding.UTF8.GetString(result.StdOut)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<(int ExitCode, byte[] StdOut)> RunAsync(
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("wsl.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start wsl.exe.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        using var stdout = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(stdout, timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await copy;
            await stderr;
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // The process may already be gone.
            }

            throw;
        }

        return (process.ExitCode, stdout.ToArray());
    }
}
