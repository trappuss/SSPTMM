using System.Text.Json;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.Core.ServerMap;

//
// Talks to a TCFMM Server Map server mod over the SPT server's own port.
//
// This works at all because of one thing, found by decompiling SPT 4.1 and then proven against a
// live server: HttpServer picks a listener with CanHandle(context) BEFORE it reads the PHPSESSID
// cookie, and dispatches regardless of whether there is a session behind it. So an ordinary desktop
// app carrying no SPT assemblies is a first-class client of a mod's routes.
//
// One instance per endpoint, disposed with it. Not safe for concurrent calls on one instance: the
// certificate callback records what it saw into state the request in flight then reads back.
//
public sealed class ServerMapClient : IDisposable
{
    // The protocol this build speaks. A server on anything else is refused, not guessed at.
    public const int SupportedProtocol = 1;

    public const string HelloPath = "/tcfservermap/hello";

    public const string ListPath = "/tcfservermap/list";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(6);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly ServerMapEndpoint _endpoint;
    private readonly HttpClient _http;
    private readonly PinRecorder _pin;

    private ServerMapClient(ServerMapEndpoint endpoint, HttpMessageHandler handler, Uri baseUri, TimeSpan timeout,
        PinRecorder pin)
    {
        _endpoint = endpoint;
        _pin = pin;
        _http = new HttpClient(handler) { BaseAddress = baseUri, Timeout = timeout };
        _pin.Trusted ??= endpoint.PinnedThumbprint;
    }

    //
    // The shared key goes only to a server already known to be this one: one whose certificate is
    // pinned (a request only goes out once TLS has checked it against the pin), or that answered this
    // client's handshake as a server map. Never on the handshake itself, which does not need it -
    // a mistyped address, or something in between on a first connection, would otherwise be handed
    // the key before anything had been checked.
    //
    private bool _answeredWithoutTls;

    private bool KeyMaySend => _endpoint.HasKey && (_pin.Trusted is not null || _answeredWithoutTls);

    private HttpRequestMessage Get(string path, bool withKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (withKey && KeyMaySend)
            request.Headers.TryAddWithoutValidation(ServerMapEndpoint.KeyHeaderName, _endpoint.SharedKey!.Trim());
        return request;
    }

    //
    // Null when the endpoint cannot be turned into a URL. The caller reports InvalidAddress rather
    // than this throwing, because a half-typed address in an Options box is not exceptional.
    //
    public static ServerMapClient? TryCreate(ServerMapEndpoint endpoint, TimeSpan? timeout = null)
    {
        if (!endpoint.TryGetBaseUri(out var baseUri)) return null;

        var pin = new PinRecorder();

        var handler = new HttpClientHandler
        {
            //
            // HttpClient honours HTTPS_PROXY / HTTP_PROXY from the environment. Left on, a user
            // behind a corporate or VPN proxy has their own LAN server address tunnelled through
            // it - and the failure arrives as a socket reset BEFORE the certificate callback ever
            // runs, which looks like anything except a proxy problem. Found the hard way during the
            // transport spike; this line is the fix and must not be dropped.
            //
            UseProxy = false,

            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            {
                pin.Presented = certificate is null ? null : ServerCertificatePin.Thumbprint(certificate);

                // Against the pin, or the certificate this client's handshake already accepted.
                pin.Verdict = ServerCertificatePin.Decide(pin.Trusted ?? endpoint.PinnedThumbprint, pin.Presented);

                return ServerCertificatePin.Accepts(pin.Verdict);
            },
        };

        return new ServerMapClient(endpoint, handler, baseUri, timeout ?? DefaultTimeout, pin);
    }

    //
    // The same client over a caller-supplied handler, so the response-handling half - status codes,
    // bodies that are not us, protocol mismatches - can be tested without a TLS stack or a
    // listening socket. No pinning happens on this path, which is why it is not public.
    //
    internal static ServerMapClient? TryCreate(ServerMapEndpoint endpoint, HttpMessageHandler handler,
        TimeSpan? timeout = null) =>
        endpoint.TryGetBaseUri(out var baseUri)
            ? new ServerMapClient(endpoint, handler, baseUri, timeout ?? DefaultTimeout, new PinRecorder())
            : null;

    //
    // The handshake. Cheap and safe to call on a timer: it is what gates the nav item, and the list
    // revision it carries is what avoids fetching a list that has not changed.
    //
    public async Task<ServerHelloProbe> HelloAsync(CancellationToken cancellationToken = default)
    {
        _pin.Reset();

        try
        {
            using var request = Get(HelloPath, withKey: false);
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // A 404 from a plain SPT server and a 503 from a stub with no payload mean the same
                // thing to us: there is no server map here.
                return Fail(ServerMapProblem.NotServerMap, statusCode: (int)response.StatusCode);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            ServerHello? hello;
            try
            {
                hello = JsonSerializer.Deserialize<ServerHello>(body, Json);
            }
            catch (JsonException)
            {
                // Something answered 200 on that path without being us.
                return Fail(ServerMapProblem.NotServerMap, statusCode: (int)response.StatusCode);
            }

            // Protocol is required and one-based, so a zero means a body that merely happened to be
            // JSON rather than a handshake.
            if (hello is null || hello.Protocol == 0)
                return Fail(ServerMapProblem.NotServerMap, statusCode: (int)response.StatusCode);

            if (hello.Protocol != SupportedProtocol)
                return Fail(ServerMapProblem.ProtocolMismatch, serverProtocol: hello.Protocol,
                    statusCode: (int)response.StatusCode);

            // Known now: its certificate from here on (first use), or - with no TLS in play - that
            // it answered as a server map.
            if (_pin.Verdict == PinVerdict.FirstUse && _pin.Presented is not null) _pin.Trusted = _pin.Presented;
            if (_pin.Presented is null) _answeredWithoutTls = true;

            return new ServerHelloProbe
            {
                Endpoint = _endpoint,
                Hello = hello,
                ActualThumbprint = _pin.Presented,
                ExpectedThumbprint = _endpoint.PinnedThumbprint,
                PinnedOnThisConnection = _pin.Verdict == PinVerdict.FirstUse,
                StatusCode = (int)response.StatusCode,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            //
            // A pin rejection surfaces here as a transport failure, because refusing the certificate
            // is what tore the connection down. The verdict recorded by the callback is the only
            // thing that can tell that apart from the server simply not being there.
            //
            if (_pin.Verdict == PinVerdict.Mismatch) return Fail(ServerMapProblem.CertificateRejected, error: ex);

            return Fail(ServerMapProblem.Unreachable, error: ex);
        }
        catch (Exception ex)
        {
            return Fail(ServerMapProblem.Failed, error: ex);
        }
    }

    //
    // The mod list this server publishes, as the `.tcfmodlist` file its operator dropped into the
    // mod's config folder - served verbatim, so what arrives here is the same format a list mailed
    // between two people is, and it is read by the same parser.
    //
    // Worth calling only when a handshake said HasList, and worth re-calling only when ListRevision
    // moves. Nothing here enforces that; it is the caller's to decide, because the caller is the one
    // that knows what revision it already holds.
    //
    public async Task<ServerMapListResult> ListAsync(CancellationToken cancellationToken = default)
    {
        _pin.Reset();

        try
        {
            // Nothing known about this server yet (never pinned, no handshake here): the handshake
            // first, so the key goes to it only once it has answered as one.
            if (_endpoint.HasKey && !KeyMaySend) await HelloAsync(cancellationToken).ConfigureAwait(false);
            _pin.Reset();

            using var request = Get(ListPath, withKey: true);
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                //
                // A 404 here is the server saying it publishes nothing, which is a normal thing for
                // a server to say. It is only "wrong address" on /hello, where nothing has yet
                // established that the mod is there at all.
                //
                // A 401 splits on whether we sent a key at all: "you need one" and "yours is wrong"
                // are different messages to the user and different next actions.
                //
                return new ServerMapListResult
                {
                    Endpoint = _endpoint,
                    Problem = response.StatusCode switch
                    {
                        System.Net.HttpStatusCode.NotFound => ServerMapProblem.NoList,
                        System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden =>
                            _endpoint.HasKey ? ServerMapProblem.KeyRejected : ServerMapProblem.KeyRequired,
                        _ => ServerMapProblem.Failed,
                    },
                    StatusCode = (int)response.StatusCode,
                };
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            //
            // Parsed by ModListFile, the same code that reads a list someone sends you. A served
            // list is not a second format and must never become one - and the address is what the
            // list records as its source, not whoever exported it. See ModListFile.Read.
            //
            var import = ModListFile.Read(body, $"{_endpoint.Host}:{_endpoint.Port}", ModListOrigin.Server);

            if (!import.Succeeded)
            {
                return new ServerMapListResult
                {
                    Endpoint = _endpoint,
                    Problem = ServerMapProblem.ListUnreadable,
                    ParseError = import.Error,
                    StatusCode = (int)response.StatusCode,
                };
            }

            return new ServerMapListResult
            {
                Endpoint = _endpoint,
                List = import.List,
                StatusCode = (int)response.StatusCode,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            return new ServerMapListResult
            {
                Endpoint = _endpoint,
                Problem = _pin.Verdict == PinVerdict.Mismatch
                    ? ServerMapProblem.CertificateRejected
                    : ServerMapProblem.Unreachable,
                Error = ex,
            };
        }
        catch (Exception ex)
        {
            return new ServerMapListResult { Endpoint = _endpoint, Problem = ServerMapProblem.Failed, Error = ex };
        }
    }

    private ServerHelloProbe Fail(ServerMapProblem problem, Exception? error = null, int? statusCode = null,
        int? serverProtocol = null) =>
        new()
        {
            Endpoint = _endpoint,
            Problem = problem,
            ActualThumbprint = _pin.Presented,
            ExpectedThumbprint = _endpoint.PinnedThumbprint,
            ServerProtocol = serverProtocol,
            StatusCode = statusCode,
            Error = error,
        };

    public void Dispose() => _http.Dispose();
}

//
// The one piece of shared state between the certificate callback and the request that provoked it.
// It exists because the callback runs inside the handler, which has to be built before the client
// that would otherwise own the fields.
//
internal sealed class PinRecorder
{
    public string? Presented { get; set; }

    // The certificate this client trusts: the pinned one, or the one its handshake accepted.
    public string? Trusted { get; set; }

    public PinVerdict Verdict { get; set; } = PinVerdict.NoCertificate;

    public void Reset()
    {
        Presented = null;
        Verdict = PinVerdict.NoCertificate;
    }
}
