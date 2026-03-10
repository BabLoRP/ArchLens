using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;

namespace ArchlensTests.Utils;

public sealed class TestHttpHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, HttpResponseMessage> _map = new(StringComparer.OrdinalIgnoreCase);

    public void When(string url, HttpStatusCode status, string? body = null, string mediaType = "application/json")
    {
        var msg = new HttpResponseMessage(status)
        {
            Content = body is null ? null : new StringContent(body, System.Text.Encoding.UTF8, mediaType)
        };
        _map[url] = msg;
    }

    public void When(string url, HttpStatusCode status, byte[]? body, string mediaType = "application/octet-stream")
    {
        var msg = new HttpResponseMessage(status);
        if (body is not null)
        {
            msg.Content = new ByteArrayContent(body);
            msg.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        }
        _map[url] = msg;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_map.TryGetValue(request.RequestUri!.ToString(), out var msg))
            return Task.FromResult(msg.Clone());

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}


internal static class HttpResponseMessageExtensions
{
    public static HttpResponseMessage Clone(this HttpResponseMessage msg)
    {
        var clone = new HttpResponseMessage(msg.StatusCode);
        if (msg.Content is not null)
        {
            var bytes = msg.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            var mediaType = msg.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            clone.Content = new ByteArrayContent(bytes);
            clone.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        }
        foreach (var h in msg.Headers) clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
        clone.RequestMessage = new HttpRequestMessage();
        return clone;
    }
}
