using System.Text;
using OpenCodeSticky.Core.Discovery;
using Xunit;

namespace OpenCodeSticky.Core.Tests;

public class ServiceDiscoveryTests
{
    private const string NativeJson = "{\"url\":\"http://127.0.0.1:28894\",\"password\":\"pw\",\"version\":\"2.2.0\"}";

    [Fact]
    public void Parse_ValidServiceFile_ReturnsUrlAndPassword()
    {
        var file = ServiceFileParser.Parse(NativeJson);

        Assert.Equal(new Uri("http://127.0.0.1:28894"), file.Url);
        Assert.Equal("pw", file.Password);
        Assert.Equal("2.2.0", file.Version);
        Assert.Null(file.Pid);
    }

    [Fact]
    public void Parse_MissingUrl_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => ServiceFileParser.Parse("{\"password\":\"pw\"}"));
    }

    [Fact]
    public void Parse_InvalidJson_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => ServiceFileParser.Parse("not json"));
    }

    [Fact]
    public void DecodeDistroList_Utf16NoBom_ReturnsNames()
    {
        var bytes = Encoding.Unicode.GetBytes("Ubuntu\r\nDebian\r\n");

        Assert.Equal(new[] { "Ubuntu", "Debian" }, WslGateway.DecodeDistroList(bytes));
    }

    [Fact]
    public void DecodeDistroList_Utf16WithBom_ReturnsNames()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("Ubuntu\r\n")).ToArray();

        Assert.Equal(new[] { "Ubuntu" }, WslGateway.DecodeDistroList(bytes));
    }

    [Fact]
    public async Task Discover_NativeAndTwoDistros_ReturnsThreeCandidates()
    {
        var nativePath = WriteNative(NativeJson);
        var wsl = new FakeWslGateway();
        wsl.Distros.AddRange(new[] { "Ubuntu", "Debian" });
        wsl.Files["Ubuntu"] = "{\"url\":\"http://127.0.0.1:49374\",\"password\":\"pw-u\"}";
        wsl.Files["Debian"] = "{\"url\":\"http://127.0.0.1:49375\",\"password\":\"pw-d\"}";

        var result = await new ServiceDiscovery(wsl, nativePath).DiscoverAsync(CancellationToken.None);

        Assert.Equal(new[] { "native", "wsl:Ubuntu", "wsl:Debian" }, result.Select(c => c.Key));
        Assert.Equal(new[] { "Windows", "Ubuntu", "Debian" }, result.Select(c => c.DisplayName));
        Assert.Equal("pw-u", result[1].Password);
    }

    [Fact]
    public async Task Discover_NoNativeFileNoDistros_ReturnsEmpty()
    {
        var missing = Path.Combine(Path.GetTempPath(), "ocs-missing-" + Guid.NewGuid() + ".json");

        var result = await new ServiceDiscovery(new FakeWslGateway(), missing).DiscoverAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Discover_DistroWithoutServiceFile_IsSkipped()
    {
        var wsl = new FakeWslGateway();
        wsl.Distros.Add("Ubuntu");
        wsl.Files["Ubuntu"] = null;

        var result = await new ServiceDiscovery(wsl, WriteNative(NativeJson)).DiscoverAsync(CancellationToken.None);

        Assert.Equal(new[] { "native" }, result.Select(c => c.Key));
    }

    [Fact]
    public async Task Discover_OneMalformedDistroFile_DoesNotHideTheOthers()
    {
        var wsl = new FakeWslGateway();
        wsl.Distros.AddRange(new[] { "Ubuntu", "Debian" });
        wsl.Files["Ubuntu"] = "{broken";
        wsl.Files["Debian"] = "{\"url\":\"http://127.0.0.1:49375\",\"password\":\"pw\"}";

        var result = await new ServiceDiscovery(wsl, WriteNative(NativeJson)).DiscoverAsync(CancellationToken.None);

        Assert.Equal(new[] { "native", "wsl:Debian" }, result.Select(c => c.Key));
    }

    [Fact]
    public async Task Discover_WslCandidate_GetsDistroIpAlternateUrl()
    {
        var wsl = new FakeWslGateway();
        wsl.Distros.Add("Ubuntu");
        wsl.Files["Ubuntu"] = "{\"url\":\"http://127.0.0.1:49374\",\"password\":\"pw\"}";
        wsl.Addresses["Ubuntu"] = "172.20.1.5";

        var result = await new ServiceDiscovery(wsl, WriteNative(NativeJson)).DiscoverAsync(CancellationToken.None);

        var ubuntu = result.Single(c => c.Key == "wsl:Ubuntu");
        Assert.Equal(new Uri("http://172.20.1.5:49374"), ubuntu.AlternateUrl);
        Assert.Null(result.Single(c => c.Key == "native").AlternateUrl);
    }

    [Fact]
    public async Task Discover_NoDistroAddress_LeavesAlternateNull()
    {
        var wsl = new FakeWslGateway();
        wsl.Distros.Add("Ubuntu");
        wsl.Files["Ubuntu"] = "{\"url\":\"http://127.0.0.1:49374\",\"password\":\"pw\"}";

        var result = await new ServiceDiscovery(wsl, WriteNative(NativeJson)).DiscoverAsync(CancellationToken.None);

        Assert.Null(result.Single(c => c.Key == "wsl:Ubuntu").AlternateUrl);
    }

    private static string WriteNative(string json)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ocs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "service.json");
        File.WriteAllText(path, json);
        return path;
    }

    private sealed class FakeWslGateway : IWslGateway
    {
        public List<string> Distros { get; } = new();
        public Dictionary<string, string?> Files { get; } = new();
        public Dictionary<string, string?> Addresses { get; } = new();

        public Task<IReadOnlyList<string>> ListDistrosAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>(Distros);

        public Task<string?> ReadServiceFileAsync(string distro, CancellationToken cancellationToken)
            => Task.FromResult(Files.GetValueOrDefault(distro));

        public Task<string?> GetDistroAddressAsync(string distro, CancellationToken cancellationToken)
            => Task.FromResult(Addresses.GetValueOrDefault(distro));
    }
}
