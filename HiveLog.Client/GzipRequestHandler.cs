using System.IO.Compression;

namespace HiveLog.Client;

/// <summary>
/// DelegatingHandler that gzip-compresses outgoing ingest request bodies. (00741)
///
/// Backend log producers are the highest-volume, highly-repetitive source — they should send compressed,
/// mirroring the frontend (native CompressionStream gzip). The HiveLog server decompresses transparently
/// (UseRequestDecompression). gzip + CompressionLevel.Fastest = throughput over ratio: logging must never
/// add latency to the calling service.
///
/// WHY a handler and NOT a change to HiveLogBackendClient: that client is NSwag/OpenAPI-generated and gets
/// regenerated — any hand-patch there would be silently lost. The handler sits on the named "hivelog"
/// HttpClient and survives regeneration.
/// </summary>
internal sealed class GzipRequestHandler : DelegatingHandler
{
    // Below this, gzip framing overhead outweighs the saving (small batches).
    private const int MinBytes = 1024;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            var raw = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (raw.Length >= MinBytes)
            {
                var contentType = request.Content.Headers.ContentType;

                using var ms = new MemoryStream();
                using (var gzip = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
                {
                    gzip.Write(raw, 0, raw.Length);
                }

                var compressed = new ByteArrayContent(ms.ToArray());
                compressed.Headers.ContentType = contentType;
                compressed.Headers.ContentEncoding.Add("gzip");
                request.Content = compressed;
            }
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
