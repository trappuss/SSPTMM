using System.Diagnostics;

namespace TCFModManager.Core.Services;

// 
// Downloads a mod/addon version archive from its sp-mod.com download link.
// 
public sealed class ModDownloadService(HttpClient? httpClient = null) : IDisposable
{
    private readonly HttpClient _http = httpClient ?? new HttpClient();
    private readonly bool _ownsHttpClient = httpClient is null;

    //
    // How often progress is reported, regardless of how fast the bytes arrive.
    //
    // The read buffer is 80 KB, so a 5.5 GB archive is about 70,000 reads. Reporting every one of
    // them posts 70,000 callbacks to the UI thread, each raising property changes and invalidating
    // layout - and no display can show more than a few dozen of those a second, so the rest is pure
    // contention against the thread that has to render the number. On a multi-gigabyte download it
    // is enough to make the whole window feel heavy.
    //
    // 100ms is finer than the eye and roughly two updates per rendered frame.
    //
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(100);

    // How long a download may go without a single byte arriving before it counts as stalled. The
    // client's own timeout stops at the response headers; a transfer that stops part way through
    // would otherwise wait for ever.
    public static TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Beside a partial download: what identifies the file it is part of (the server's ETag
    /// or Last-Modified), so a resumed download is only ever joined to the same file.</summary>
    public static string ValidatorPathFor(string partPath) => partPath + ".validator";

    // Downloads <paramref name="downloadUrl"/> to <paramref name="destinationPath"/>,
    // reporting fractional progress (0.0-1.0) when a Content-Length header is available. Returns the
    // file name the server offered, for a caller that saves the archive under a name of its own.
    //
    // Fork - <paramref name="resumable"/>: what identifies the file (the server's ETag or Last-Modified,
    // and its full length) is kept beside it while it downloads, so a later attempt can resume it.
    // <paramref name="resume"/>: when part of the file is already there from an attempt that broke
    // off, only the rest is asked for - and joined on only when the server sends exactly the rest
    // of exactly the same file. Anything else is thrown away and started again.
    public async Task<DownloadResponse> DownloadAsync(
        string downloadUrl,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default,
        bool resume = false,
        bool resumable = false)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var validatorPath = ValidatorPathFor(destinationPath);
        var have = resume && File.Exists(destinationPath) ? new FileInfo(destinationPath).Length : 0;
        var saved = have > 0 ? ReadValidator(validatorPath) : null;
        if (saved is null) have = 0;

        async Task<HttpResponseMessage> SendAsync(long from)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
            if (from > 0)
            {
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(from, null);
                request.Headers.TryAddWithoutValidation("If-Range", saved!.Value.Identity);
            }

            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }

        // Nothing left to send (416), or a range that is not exactly the rest of the same file.
        bool Unusable(HttpResponseMessage r, long from) =>
            r.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable
            || (r.StatusCode == System.Net.HttpStatusCode.PartialContent
                && !(from > 0 && r.Content.Headers.ContentRange is { From: { } start, To: { } to, Length: { } length }
                     && start == have && to == length - 1 && length == saved!.Value.Length));

        var response = await SendAsync(have).ConfigureAwait(false);

        //
        // What is on disk cannot be trusted to join, so it goes - and the whole file is asked for
        // again at once, rather than spending one of the queue's retries on it.
        //
        if (Unusable(response, have))
        {
            AppLog.Info("Downloads", $"{Path.GetFileName(destinationPath)}: the server did not send the rest as asked ({(int)response.StatusCode}); starting again");
            response.Dispose();
            TryDelete(destinationPath);
            TryDelete(validatorPath);
            have = 0;

            response = await SendAsync(0).ConfigureAwait(false);
            if (Unusable(response, 0))
            {
                var status = (int)response.StatusCode;
                response.Dispose();
                throw new HttpIOException(HttpRequestError.ResponseEnded, $"the server sent part of the file when all of it was asked for ({status})");
            }
        }

        using var _ = response;
        var range = response.Content.Headers.ContentRange;

        response.EnsureSuccessStatusCode();

        var offeredName = OfferedFileName(response);

        var resuming = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
        if (!resuming) have = 0;

        var totalBytes = resuming
            ? range!.Length
            : response.Content.Headers.ContentLength;

        // What identifies this file, for a later attempt to resume against.
        if (!resuming && (resumable || resume))
        {
            // A strong ETag, or with none at all Last-Modified; a weak one says the file may differ
            // byte for byte, and a date may not stand in for it (RFC 9110 13.1.5) - not resumable.
            var identity = response.Headers.ETag switch
            {
                { IsWeak: false } etag => etag.ToString(),
                null => response.Content.Headers.LastModified?.ToString("R"),
                _ => null,
            };

            if (identity is not null && totalBytes is > 0 && response.Headers.AcceptRanges.Contains("bytes"))
                WriteValidator(validatorPath, identity, totalBytes.Value);
            else
                TryDelete(validatorPath);
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var fileStream = new FileStream(
            destinationPath, resuming ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);

        var buffer = new byte[81920];
        var totalRead = have;
        int read;

        var clock = Stopwatch.StartNew();
        var nextReport = TimeSpan.Zero;

        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        idle.CancelAfter(IdleTimeout);

        try
        {
            while ((read = await contentStream.ReadAsync(buffer, idle.Token).ConfigureAwait(false)) > 0)
            {
                idle.CancelAfter(IdleTimeout);
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                totalRead += read;

                if (totalBytes is > 0 && clock.Elapsed >= nextReport)
                {
                    nextReport = clock.Elapsed + ReportInterval;
                    progress?.Report((double)totalRead / totalBytes.Value);
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Stalled, not cancelled: a failure that may not happen again, retried as one.
            throw new HttpIOException(HttpRequestError.ResponseEnded,
                $"nothing arrived for {IdleTimeout.TotalSeconds:F0} seconds ({totalRead:N0} bytes in)");
        }

        //
        // A short read loop is not an error to HttpClient: a connection closed cleanly part way
        // through leaves a truncated file behind and returns normally. That file then extracts as
        // far as it goes - a zip throws on its missing central directory, but a tar or a solid
        // archive can stop quietly, having written some of the mod's files and none of the rest.
        //
        // So the promised length is checked here, where both numbers are in hand, rather than
        // trusting whatever landed on disk. Short only, never long: a handler configured to
        // decompress transparently would read more bytes than the header promised, and that is a
        // complete download, not a failure.
        //
        if (totalBytes is { } expected && totalRead < expected)
        {
            throw new ModInstallException(ModInstallFailure.DownloadIncomplete)
            {
                ExpectedBytes = expected,
                ReceivedBytes = totalRead,
            };
        }

        TryDelete(validatorPath);

        // Unconditional, so the throttle above can never swallow the last fraction and leave a bar
        // stopped short of the end.
        progress?.Report(1.0);

        return new DownloadResponse(offeredName, totalRead);
    }

    //
    // The Content-Disposition file name, preferring the RFC 5987 filename* form, which is the one
    // that carries non-ASCII names intact. Only the last path segment is kept: the header is the
    // server's to write, and a name holding a folder is not one to follow out of the folder chosen.
    //
    private static string? OfferedFileName(HttpResponseMessage response)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        var name = disposition?.FileNameStar ?? disposition?.FileName;
        if (string.IsNullOrWhiteSpace(name)) return null;

        name = name.Trim().Trim('"').Replace('\\', '/');
        var slash = name.LastIndexOf('/');
        if (slash >= 0) name = name[(slash + 1)..];

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static (string Identity, long Length)? ReadValidator(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var lines = File.ReadAllLines(path);
            return lines.Length >= 2 && lines[0].Length > 0 && long.TryParse(lines[1], out var length) && length > 0
                ? (lines[0], length)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not resumable this time, then - never a reason for the download itself to fail.
            return null;
        }
    }

    private static void WriteValidator(string path, string identity, long length)
    {
        try
        {
            File.WriteAllLines(path, [identity, length.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Debug("Downloads", $"couldn't keep what identifies {Path.GetFileName(path)}; it will not resume: {ex.Message}");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Debug("Downloads", $"couldn't remove {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }
}

// What a finished download reported: the name the server offered for the file, if any, and how many
// bytes arrived.
public sealed record DownloadResponse(string? OfferedFileName, long Bytes);
