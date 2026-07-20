namespace LexVerse.Infrastructure.Commerce;

internal static class HttpJson
{
    public static async Task<byte[]> ReadBoundedAsync(
        HttpClient client,
        HttpRequestMessage request,
        int maximumBytes,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using (request)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            deadline.CancelAfter(timeout);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length > maximumBytes)
            {
                throw new InvalidOperationException("The remote response is too large.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var output = new MemoryStream();
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, deadline.Token);
                if (read == 0)
                {
                    return output.ToArray();
                }

                if (output.Length + read > maximumBytes)
                {
                    throw new InvalidOperationException("The remote response is too large.");
                }

                output.Write(buffer, 0, read);
            }
        }
    }
}
