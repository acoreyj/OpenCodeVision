using System.Runtime.CompilerServices;
using System.Text;

namespace OpenCodeSticky.Core;

public static class SseParser
{
    public static async IAsyncEnumerable<string> ReadDataAsync(
        TextReader reader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var data = new StringBuilder();

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    yield return data.ToString();
                    data.Clear();
                }

                continue;
            }

            if (line[0] == ':')
            {
                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                var value = line[5..];
                if (value.StartsWith(' '))
                {
                    value = value[1..];
                }

                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                data.Append(value);
            }
        }

        if (data.Length > 0)
        {
            yield return data.ToString();
        }
    }
}
