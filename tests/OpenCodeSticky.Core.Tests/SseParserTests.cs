using OpenCodeSticky.Core;
using Xunit;

namespace OpenCodeSticky.Core.Tests;

public class SseParserTests
{
    [Fact]
    public async Task ReadsTwoFramesFromOneStream()
    {
        var payloads = await ReadAsync("data: {\"a\":1}\n\ndata: {\"b\":2}\n\n");

        Assert.Equal(new[] { "{\"a\":1}", "{\"b\":2}" }, payloads);
    }

    [Fact]
    public async Task IgnoresHeartbeatComments()
    {
        var payloads = await ReadAsync(": heartbeat\n\ndata: x\n\n");

        Assert.Equal(new[] { "x" }, payloads);
    }

    [Fact]
    public async Task SkipsEmptyDataLines()
    {
        var payloads = await ReadAsync("data:\n\ndata: x\n\n");

        Assert.Equal(new[] { "x" }, payloads);
    }

    [Fact]
    public async Task JoinsMultiLineData()
    {
        var payloads = await ReadAsync("data: one\ndata: two\n\n");

        Assert.Equal(new[] { "one\ntwo" }, payloads);
    }

    private static async Task<List<string>> ReadAsync(string text)
    {
        var payloads = new List<string>();
        await foreach (var payload in SseParser.ReadDataAsync(new StringReader(text), CancellationToken.None))
        {
            payloads.Add(payload);
        }

        return payloads;
    }
}
