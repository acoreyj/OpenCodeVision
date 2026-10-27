using System.Text.Json;

namespace OpenCodeVision.Core.Discovery;

public sealed record ServiceFile(Uri Url, string Password, string? Version, int? Pid);

public static class ServiceFileParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static ServiceFile Parse(string json)
    {
        ServiceFileDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ServiceFileDto>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new FormatException("Service file is not valid JSON.", ex);
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.Url) || string.IsNullOrWhiteSpace(dto.Password))
        {
            throw new FormatException("Service file is missing a URL or password.");
        }

        if (!Uri.TryCreate(dto.Url, UriKind.Absolute, out var url))
        {
            throw new FormatException("Service file URL is not absolute.");
        }

        return new ServiceFile(url, dto.Password, dto.Version, dto.Pid);
    }

    private sealed class ServiceFileDto
    {
        public string? Url { get; set; }
        public string? Password { get; set; }
        public string? Version { get; set; }
        public int? Pid { get; set; }
    }
}
