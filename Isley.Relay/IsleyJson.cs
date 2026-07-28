using System.Text.Json;
using System.Text.Json.Serialization;

namespace Isley.Relay;

internal static class IsleyJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 12
    };
}

internal static class RequestBodyReader
{
    internal static async Task<byte[]> ReadAsync(
        HttpRequest request,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > maximumBytes)
        {
            throw new BadHttpRequestException("Request body is too large.", StatusCodes.Status413PayloadTooLarge);
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
                throw new BadHttpRequestException("Request body is too large.", StatusCodes.Status413PayloadTooLarge);
            }
            await buffer.WriteAsync(block.AsMemory(0, read), cancellationToken);
        }
        return buffer.ToArray();
    }
}
