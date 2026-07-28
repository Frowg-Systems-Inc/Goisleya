using System.Text.Json;
using System.Text.Json.Serialization;

namespace Isley.ServerBridge;

internal static class BridgeJson
{
    internal static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            MaxDepth = 12
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

internal static class PluginRequestBodyReader
{
    internal static async Task<byte[]> ReadAsync(
        HttpRequest request,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > maximumBytes)
        {
            throw new InvalidDataException("Plugin telemetry body is too large.");
        }

        await using var buffer = new MemoryStream(Math.Min(maximumBytes, 64 * 1024));
        var block = new byte[16 * 1024];
        while (true)
        {
            var read = await request.Body.ReadAsync(block, cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (buffer.Length + read > maximumBytes)
            {
                throw new InvalidDataException("Plugin telemetry body is too large.");
            }
            await buffer.WriteAsync(block.AsMemory(0, read), cancellationToken);
        }
        return buffer.ToArray();
    }
}
