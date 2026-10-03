using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TCFModManager.Core.Services;

//
// Fork (SSPTMM): the SPT server's launcher routes, as SPT's own launcher calls them - for starting
// the game from SSPTMM without that launcher (SptDirectLaunch, experimental).
//
// Read from SPT 4.1.6's sources (SP-Tushonka/launcher SPTushonka.Core: HttpHelper, SessionHelper,
// Urls; SP-Tushonka/server-csharp LauncherV2StaticRouter, LauncherV2Callbacks):
//   - HTTPS to the server's ip:port. The server's certificate is its own self-signed one; SPT's
//     launcher accepts any certificate. This accepts one only from an address on this PC - the
//     server it starts is local, and nothing here is ever pointed elsewhere.
//   - Every JSON body, both ways, is zlib-wrapped (the launcher's SimpleZlib); bundle files are not.
//   - Answers are { "Response": ... } (plus "Profiles" after register, remove and wipe), the
//     profile objects in them lower-case ("username", "profileId"...).
//   - Requests that carry a body are PUTs.
//
public sealed class SptLauncherApi : IDisposable
{
    public const string PingPath = "/launcher/v2/ping";
    public const string TypesPath = "/launcher/v2/types";
    public const string LoginPath = "/launcher/v2/login";
    public const string RegisterPath = "/launcher/v2/register";
    public const string RemovePath = "/launcher/v2/remove";
    public const string VersionPath = "/launcher/v2/version";
    public const string ProfilesPath = "/launcher/v2/profiles";
    public const string ProfilePath = "/launcher/v2/profile";
    public const string WipePath = "/launcher/v2/wipe";
    public const string BundlesPath = "/singleplayer/bundles";
    public const string BundleFilePath = "/files/bundle/";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly HttpClient _http;

    /// <summary>"127.0.0.1:6969" - what goes after https:// for both these calls and the game.</summary>
    public string Address { get; }

    public SptLauncherApi(string address, HttpMessageHandler? handler = null)
    {
        Address = address;
        _http = new HttpClient(handler ?? LocalHandler(), disposeHandler: true)
        {
            BaseAddress = new Uri("https://" + address),
            Timeout = TimeSpan.FromSeconds(15),
        };
    }

    // SPT's server certificate is self-signed, so the usual check can never pass. Accepted from an
    // address on this PC only.
    private static SocketsHttpHandler LocalHandler() => new()
    {
        UseProxy = false,
        UseCookies = false,
        SslOptions = new SslClientAuthenticationOptions
        {
            // Safe only because ConnectCallback below refuses to connect anywhere but this PC.
            RemoteCertificateValidationCallback = (_, _, _, _) => true,
        },
        ConnectCallback = async (context, ct) =>
        {
            // The address is resolved here, so the certificate is only ever waved through for a
            // connection that really goes to this machine.
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct).ConfigureAwait(false);
            var local = addresses.FirstOrDefault(IsThisMachine)
                ?? throw new HttpRequestException($"{context.DnsEndPoint.Host} is not an address on this PC");

            var socket = new System.Net.Sockets.Socket(local.AddressFamily, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp)
            {
                NoDelay = true,
            };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(local, context.DnsEndPoint.Port), ct).ConfigureAwait(false);
                return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    /// <summary>True for a loopback address or one of this PC's own.</summary>
    public static bool IsThisMachine(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;

        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Any(u => u.Address.Equals(address));
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }

    public async Task<bool> PingAsync(CancellationToken ct = default) =>
        (await GetAsync<Answer<string>>(PingPath, ct).ConfigureAwait(false))?.Response == "Pong!";

    /// <summary>The server's version tag: "4.1.6", or "4.1.6 - (text)" on a bleeding-edge build.</summary>
    public async Task<string?> VersionAsync(CancellationToken ct = default) =>
        (await GetAsync<Answer<string>>(VersionPath, ct).ConfigureAwait(false))?.Response;

    public async Task<IReadOnlyList<SptMiniProfile>> ProfilesAsync(CancellationToken ct = default) =>
        (await GetAsync<Answer<List<SptMiniProfile>>>(ProfilesPath, ct).ConfigureAwait(false))?.Response ?? [];

    /// <summary>Profile type (edition) name to its description, as New profile offers them.</summary>
    public async Task<IReadOnlyDictionary<string, string>> TypesAsync(CancellationToken ct = default) =>
        (await GetAsync<Answer<Dictionary<string, string>>>(TypesPath, ct).ConfigureAwait(false))?.Response ?? [];

    /// <summary>True when the server has a profile by this name. The server checks only that.</summary>
    public async Task<bool> LoginAsync(string username, CancellationToken ct = default) =>
        (await PutAsync<Answer<bool>>(LoginPath, new { username }, ct).ConfigureAwait(false))?.Response == true;

    /// <summary>False when the name is taken or the edition is not offered.</summary>
    public async Task<bool> RegisterAsync(string username, string edition, CancellationToken ct = default) =>
        (await PutAsync<Answer<bool>>(RegisterPath, new { username, edition }, ct).ConfigureAwait(false))?.Response == true;

    public async Task<bool> RemoveAsync(string username, CancellationToken ct = default) =>
        (await PutAsync<Answer<bool>>(RemovePath, new { username }, ct).ConfigureAwait(false))?.Response == true;

    /// <summary>Marks the profile for a fresh start (the game then makes a new character). False
    /// when the server's config forbids wiping, or there is no such profile.</summary>
    public async Task<bool> WipeAsync(string username, string edition, CancellationToken ct = default) =>
        (await PutAsync<Answer<bool>>(WipePath, new { username, edition }, ct).ConfigureAwait(false))?.Response == true;

    public async Task<IReadOnlyList<SptBundleEntry>> BundlesAsync(CancellationToken ct = default) =>
        await GetAsync<List<SptBundleEntry>>(BundlesPath, ct).ConfigureAwait(false) ?? [];

    /// <summary>One bundle file, streamed to <paramref name="destination"/> through a .part file so a
    /// cut-off download is never taken for a whole one.</summary>
    public async Task DownloadBundleAsync(string fileName, string destination, Action<long>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var part = destination + ".part";

        try
        {
            using var response = await _http.GetAsync(BundleFilePath + fileName, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var target = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    progress?.Invoke(read);
                }
            }

            File.Move(part, destination, overwrite: true);
        }
        catch
        {
            try { File.Delete(part); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct).ConfigureAwait(false);
        return Read<T>(await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false));
    }

    private async Task<T?> PutAsync<T>(string path, object body, CancellationToken ct)
    {
        using var content = new ByteArrayContent(Compress(JsonSerializer.Serialize(body)));
        using var response = await _http.PutAsync(path, content, ct).ConfigureAwait(false);
        return Read<T>(await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false));
    }

    internal static T? Read<T>(byte[] body) => JsonSerializer.Deserialize<T>(Decompress(body), Json);

    // zlib (RFC 1950): a 0x78 first byte. Anything else - an error page - is read as it is.
    internal static string Decompress(byte[] body)
    {
        if (body.Length < 2 || body[0] != 0x78) return Encoding.UTF8.GetString(body);

        using var input = new MemoryStream(body);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    internal static byte[] Compress(string json)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            zlib.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }

    private sealed record Answer<T>
    {
        public T? Response { get; init; }
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>A profile as the launcher routes list it.</summary>
public sealed record SptMiniProfile
{
    [JsonPropertyName("username")]
    public string Username { get; init; } = "";

    [JsonPropertyName("nickname")]
    public string Nickname { get; init; } = "";

    [JsonPropertyName("side")]
    public string Side { get; init; } = "";

    [JsonPropertyName("currlvl")]
    public int Level { get; init; }

    [JsonPropertyName("profileId")]
    public string ProfileId { get; init; } = "";

    [JsonPropertyName("edition")]
    public string Edition { get; init; } = "";

    // True for a new or wiped profile: the game makes its character on the next start.
    [JsonPropertyName("wipe")]
    public bool Wipe { get; init; }

    [JsonPropertyName("invalidOrUnloadableProfile")]
    public bool Invalid { get; init; }
}

/// <summary>One entry of the server's bundle list (SPT 4.1.3 and later).</summary>
public sealed record SptBundleEntry
{
    public string FileName { get; init; } = "";

    public string ModPath { get; init; } = "";

    public uint Crc { get; init; }

    public long Size { get; init; }

    public long ModifiedUtcTicks { get; init; }
}
