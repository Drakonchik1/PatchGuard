using System.IO;
using System.Net.Http;

namespace PatchGuard.Services.Ai;

internal static class BoundedHttpResponse
{
    internal const int MaxBodyBytes = 1024 * 1024;

    public static async Task<Stream> ReadAsStreamAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > MaxBodyBytes)
        {
            throw CreateOversizedResponseException();
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        var destination = new MemoryStream(
            response.Content.Headers.ContentLength is > 0 and <= MaxBodyBytes
                ? (int)response.Content.Headers.ContentLength.Value
                : 0);
        var buffer = new byte[81_920];
        var totalBytes = 0;

        try
        {
            while (true)
            {
                var bytesToRead = Math.Min(buffer.Length, MaxBodyBytes - totalBytes + 1);
                var bytesRead = await source.ReadAsync(
                    buffer.AsMemory(0, bytesToRead),
                    cancellationToken);
                if (bytesRead == 0)
                {
                    destination.Position = 0;
                    return destination;
                }

                totalBytes += bytesRead;
                if (totalBytes > MaxBodyBytes)
                {
                    throw CreateOversizedResponseException();
                }

                await destination.WriteAsync(
                    buffer.AsMemory(0, bytesRead),
                    cancellationToken);
            }
        }
        catch
        {
            await destination.DisposeAsync();
            throw;
        }
    }

    private static InvalidDataException CreateOversizedResponseException() =>
        new($"Chat provider response exceeds the {MaxBodyBytes}-byte limit.");
}
