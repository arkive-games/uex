using System.IO.Compression;
using System.Net;
using CUE4Parse.Compression;

namespace Uex.Core;

/// <summary>
/// Fetches the Oodle and zlib-ng DLLs into .uex-cache before CUE4Parse is asked to load them.
/// CUE4Parse would download them itself, but its client sets <c>UseProxy = false</c>, so it
/// cannot reach GitHub from a network that needs one, and its 30 s timeout covers only the
/// response headers, so a body that stalls hangs the process forever with no output. This
/// client honours HTTPS_PROXY/HTTP_PROXY and the system proxy, bounds the whole download,
/// writes through a .part file (an interrupted download never looks like a valid DLL), and
/// fails with a message instead of a silent warning.
/// </summary>
internal static class NativeDownloads
{
    private static readonly TimeSpan Budget = TimeSpan.FromMinutes(3);

    public static void Ensure(string cache)
    {
        var oodle = Path.Combine(cache, OodleHelper.OodleFileName);
        var zlib = Path.Combine(cache, ZlibHelper.DllName);
        if (File.Exists(oodle) && File.Exists(zlib)) return;

        using var client = new HttpClient(new SocketsHttpHandler
        {
            Proxy = HttpClient.DefaultProxy, // HTTPS_PROXY / HTTP_PROXY env, else the system proxy
            UseProxy = true,
            AutomaticDecompression = DecompressionMethods.All,
        }) { Timeout = Timeout.InfiniteTimeSpan }; // the token below bounds the body as well
        client.DefaultRequestHeaders.UserAgent.ParseAdd("uex");

        if (!File.Exists(oodle))
            Fetch(oodle, "the OodleUE release", (part, ct) =>
                OodleHelper.DownloadOodleDllFromOodleUEAsync(client, part, ct));
        if (!File.Exists(zlib))
            Fetch(zlib, ZlibHelper.DOWNLOAD_URL, async (part, ct) =>
            {
                using var response = await client.GetAsync(ZlibHelper.DOWNLOAD_URL, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                await using var body = await response.Content.ReadAsStreamAsync(ct);
                await using var gzip = new GZipStream(body, CompressionMode.Decompress);
                await using var file = File.Create(part);
                await gzip.CopyToAsync(file, ct);
                return true;
            });
    }

    private static void Fetch(string path, string source, Func<string, CancellationToken, Task<bool>> download)
    {
        var part = path + ".part";
        Console.Error.WriteLine($"uex: downloading {Path.GetFileName(path)} from {source}");
        using var cts = new CancellationTokenSource(Budget);
        bool ok;
        string? reason = null;
        try
        {
            ok = download(part, cts.Token).GetAwaiter().GetResult() && File.Exists(part) && new FileInfo(part).Length > 0;
        }
        catch (Exception e)
        {
            ok = false;
            reason = e.Message;
        }
        if (!ok && cts.IsCancellationRequested)
            reason = $"timed out after {Budget.TotalMinutes:0} minutes"; // the Oodle helper swallows the cancellation
        if (!ok)
        {
            File.Delete(part);
            throw new UexException(
                $"Could not download {Path.GetFileName(path)} ({reason ?? "download failed"}). " +
                $"If GitHub needs a proxy here, set HTTPS_PROXY (e.g. http://127.0.0.1:7897), " +
                $"or copy the DLL into {Path.GetDirectoryName(path)} by hand.");
        }
        File.Move(part, path, overwrite: true);
    }
}
