namespace TCFModManager.Core.ServerMap;

//
// Why a probe did not produce a handshake. The App words it; this reports what happened and the
// values behind it, per the convention that Core owns no user-facing prose.
//
public enum ServerMapProblem
{
    None,

    // Nothing configured yet. Not an error - the feature is simply unused.
    NoAddress,

    // The host or port could not be turned into a URL. Carries the endpoint.
    InvalidAddress,

    // Nothing answered - DNS, refused, timed out. Carries Error.
    Unreachable,

    // A certificate was presented that is not the pinned one. Carries ExpectedThumbprint and
    // ActualThumbprint, so the caller can name both and offer to re-pin.
    CertificateRejected,

    // Something answered, but not this mod. A 404 from a plain SPT server and a 503 from a stub
    // whose payload folder has been emptied both land here - and neither is worth alarming anyone
    // with, they just mean "no server map at this address".
    NotServerMap,

    // A Server Map, speaking a protocol this build does not know. Carries ServerProtocol.
    ProtocolMismatch,

    //
    // /list only: the server runs the mod and deliberately publishes nothing. Distinct from
    // NotServerMap even though both arrive as a 404, because they mean opposite things - one is
    // "wrong address", the other is "right address, nothing on offer".
    //
    NoList,

    // /list only: a list came back and could not be read. Carries ParseError, which names why.
    ListUnreadable,

    //
    // The server refused the request for want of a key. Split in two because the two need different
    // sentences: one asks the operator for a key, the other says the one you have is wrong.
    //
    KeyRequired,

    KeyRejected,

    //
    // The map routes only: a Server Map older than 0.2.0, which serves the list but has no map.
    // Reached, keyed, and simply without the route - worth saying as "update the server mod", not
    // as a failure.
    //
    MapUnsupported,

    //
    // The server answers its own network only (LAN-only), and this machine is outside it. Said
    // outright rather than as "no server map here": the address is right and the mod is there, and
    // the fix is the operator's or a VPN such as Tailscale.
    //
    LanOnly,

    // Anything else. Carries Error.
    Failed,
}

public sealed record ServerHelloProbe
{
    public required ServerMapEndpoint Endpoint { get; init; }

    public ServerHello? Hello { get; init; }

    public ServerMapProblem Problem { get; init; }

    // What the server actually presented, recorded even when the pin rejected it - that is what
    // makes "re-pin to this one?" possible instead of a dead end.
    public string? ActualThumbprint { get; init; }

    public string? ExpectedThumbprint { get; init; }

    // True when this connection established the pin rather than checking one, which is the moment
    // the caller has to persist ActualThumbprint.
    public bool PinnedOnThisConnection { get; init; }

    public int? ServerProtocol { get; init; }

    public int? StatusCode { get; init; }

    public Exception? Error { get; init; }

    public bool Found => Hello is not null;
}

//
// The outcome of asking a server for its published list. Separate from ServerHelloProbe because the
// two questions fail differently: a handshake can be "not a server map", a list can be "no list
// published" or "a list that will not parse", and flattening those into one type would mean every
// caller checking which half of it is meaningful.
//
public sealed record ServerMapListResult
{
    public required ServerMapEndpoint Endpoint { get; init; }

    // Origin is already Server and Source is already the address it came from - see ModListFile.Read.
    public Models.ModList? List { get; init; }

    public ServerMapProblem Problem { get; init; }

    // Why the list would not parse, in ModListFile's own words. Set only for ListUnreadable.
    public string? ParseError { get; init; }

    public int? StatusCode { get; init; }

    public Exception? Error { get; init; }

    public bool Found => List is not null;
}
