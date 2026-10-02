using System.Globalization;
using TCFModManager.App.Localization;
using TCFModManager.Core.Models;
using TCFModManager.Core.ServerMap;

namespace TCFModManager.App.Services;

//
// What the user is told when a Server Map handshake doesn't come back. ServerMapClient reports a
// ServerMapProblem plus the values behind it and stops there; the wording lives here, beside the
// rest of this app's prose, for the same reason ModInstallProblems and SptLaunchProblems do.
//
// The tone is deliberate. Most of these are not faults - a wrong port, a server that isn't up, an
// install without the mod. Only the certificate case is worth a warning, and it says what actually
// changed rather than the word "insecure".
//
// Every sentence is one key. What this file used to do - append ", running SPT 3.10" to one
// sentence, splice "3 mods" into another, glue ": reason" onto the end of a third - is the shape
// D8 rules out, because none of those pieces can be placed by somebody translating them alone.
//
public static class ServerMapProblems
{
    public static string Describe(ServerHelloProbe probe) => probe.Problem switch
    {
        ServerMapProblem.None => probe.Hello is null ? string.Empty : Connected(probe),

        ServerMapProblem.NoAddress => Strings.ServerMap_NoAddress,

        ServerMapProblem.InvalidAddress => Format(
            Strings.ServerMap_InvalidAddressFormat, probe.Endpoint.Host, probe.Endpoint.Port),

        ServerMapProblem.Unreachable => Format(
            Strings.ServerMap_UnreachableFormat, probe.Endpoint.Host, probe.Endpoint.Port),

        //
        // Both fingerprints are named because this is the one case where the user has to make a
        // judgement, and they cannot make it without seeing what changed. A mismatch is not
        // necessarily an attack: a server rebuilt from scratch, or reached through a tunnel that
        // terminates TLS itself, presents a different certificate for entirely ordinary reasons.
        //
        ServerMapProblem.CertificateRejected => Format(
            Strings.ServerMap_CertificateChangedFormat,
            probe.Endpoint.Host,
            Short(probe.ExpectedThumbprint),
            Short(probe.ActualThumbprint)),

        //
        // A plain SPT server 404s the route and a stub whose payload is missing 503s it - the same
        // answer either way, so the wording covers both without guessing which.
        //
        ServerMapProblem.LanOnly => Strings.ServerMap_LanOnly,

        ServerMapProblem.NotServerMap => Format(
            Strings.ServerMap_NotServerMapFormat, probe.Endpoint.Host, probe.Endpoint.Port),

        ServerMapProblem.ProtocolMismatch when probe.ServerProtocol > ServerMapClient.SupportedProtocol =>
            Format(Strings.ServerMap_ProtocolNewerFormat, probe.ServerProtocol, ServerMapClient.SupportedProtocol),

        ServerMapProblem.ProtocolMismatch =>
            Format(Strings.ServerMap_ProtocolOlderFormat, probe.ServerProtocol, ServerMapClient.SupportedProtocol),

        // The inner exception is the only thing that says why, so it is quoted rather than summarised.
        _ => Format(
            Strings.ServerMap_ReachFailedFormat, probe.Endpoint.Host, probe.Endpoint.Port, probe.Error?.Message),
    };

    //
    // The resting line beside the address box - what this app currently knows about that server,
    // before anything is clicked. Kept here beside the failure wording so the page never says one
    // thing in its status line and another in its message.
    //
    public static string DescribeState(ServerHelloProbe? probe, bool configured) => probe switch
    {
        null when !configured => Strings.ServerMap_NotSetUp,
        null => Strings.ServerMap_NotConnected,
        _ => Describe(probe),
    };

    //
    // What happened when the published list was fetched. Separate from Describe because a list
    // failing is not the connection failing - the server answered, which is most of what the user
    // cares about, and the list is the part that went wrong.
    //
    // servingOwnList is the operator looking at their own server: what came back is the list this
    // machine published, so it is already in their mod lists and still theirs to edit. Saying
    // "saved in your mod lists" there would read as a second copy having appeared.
    //
    public static string DescribeList(ServerMapListResult result, ModList? held, bool servingOwnList = false)
        => result.Problem switch
    {
        ServerMapProblem.None when result.List is not null && servingOwnList =>
            Strings.ServerMap_ServingOwnList(
                result.List.Entries.Count,
                result.List.Name,
                result.List.Revision,
                result.List.Entries.Count),

        ServerMapProblem.None when result.List is not null =>
            Strings.ServerMap_ListSaved(
                result.List.Entries.Count,
                result.List.Name,
                result.List.Revision,
                result.List.Entries.Count),

        //
        // Not a failure. A server can run the mod and deliberately publish nothing.
        //
        // Deliberately NOT worded as "has stopped publishing". The held list says only that some
        // server published one once, and the app cannot tell one server from another behind a
        // changed address - so the likeliest reading of this state is that the list came from a
        // DIFFERENT server, which is exactly what it looks like when someone runs two installs and
        // points the app at the second. Announcing a change nobody made sends them looking for an
        // unpublish that never happened.
        //
        ServerMapProblem.NoList when held is not null =>
            Format(Strings.ServerMap_NoListHeldFormat, held.Name),

        ServerMapProblem.NoList => Strings.ServerMap_NoList,

        //
        // Two sentences for one status code, because the next action is different. One is "go ask
        // someone", the other is "what you were given is wrong or has been changed".
        //
        ServerMapProblem.KeyRequired => Strings.ServerMap_KeyRequired,

        ServerMapProblem.KeyRejected => Strings.ServerMap_KeyRejected,

        ServerMapProblem.LanOnly => Strings.ServerMap_LanOnly,

        ServerMapProblem.ListUnreadable => Format(Strings.ServerMap_ListUnreadableFormat, result.ParseError),

        // Also what a list fetch without a handshake of its own reports when nothing answered.
        ServerMapProblem.Unreachable => Format(
            Strings.ServerMap_UnreachableFormat, result.Endpoint.Host, result.Endpoint.Port),

        ServerMapProblem.NotServerMap => Format(
            Strings.ServerMap_NotServerMapFormat, result.Endpoint.Host, result.Endpoint.Port),

        ServerMapProblem.CertificateRejected => Strings.ServerMap_ListCertificateRejected,

        _ => result.Error is null
            ? Strings.ServerMap_ListFailed
            : Format(Strings.ServerMap_ListFailedReasonFormat, result.Error.Message),
    };

    //
    // How the key is described in Options. Says what it is and is not, because the word "key"
    // invites people to treat it as a password and reuse one.
    //
    //
    // Why the map could not be read. A server mod older than 0.2.0 is not a failure - it is a server
    // that has no map yet, and the operator is the one who can change that.
    //
    public static string DescribeMap(ServerMapProblem problem) => problem switch
    {
        ServerMapProblem.MapUnsupported => Strings.ServerMap_MapUnsupported,
        ServerMapProblem.LanOnly => Strings.ServerMap_LanOnly,
        ServerMapProblem.KeyRequired => Strings.ServerMap_KeyRequired,
        ServerMapProblem.KeyRejected => Strings.ServerMap_KeyRejected,
        _ => Strings.ServerMap_MapUnavailable,
    };

    public static string DescribeKey(bool hasKey, bool serverRequiresKey) => (hasKey, serverRequiresKey) switch
    {
        (false, true) => Strings.ServerMap_KeyNeeded,
        (false, false) => Strings.ServerMap_KeyOptional,
        (true, _) => Strings.ServerMap_KeyHeld,
    };

    //
    // What a server says it publishes, before anything is fetched.
    //
    // Four whole phrases rather than one built from a name, a revision and an optional size: the
    // brackets, the order and the commas inside them are not the same in every language, and a
    // piece like ", 3 mods" cannot be placed by anybody translating it on its own.
    //
    public static string DescribePublished(ServerHello hello)
    {
        if (!hello.HasList) return Strings.ServerMap_PublishesNothing;

        var revision = hello.ListRevision?.ToString(CultureInfo.CurrentCulture) ?? Strings.Common_Unknown;
        var named = !string.IsNullOrWhiteSpace(hello.ListName);

        return (named, hello.ListEntryCount) switch
        {
            (true, { } count) =>
                Strings.ServerMap_PublishedNamed(count, hello.ListName, revision, count),
            (true, null) => Format(Strings.ServerMap_PublishedNamedFormat, hello.ListName, revision),
            (false, { } count) => Strings.ServerMap_PublishedUnnamed(count, revision, count),
            _ => Format(Strings.ServerMap_PublishedUnnamedFormat, revision),
        };
    }

    //
    // How the pin is described in Options. Deliberately says what it is FOR: on its own, a hex
    // string in a settings page means nothing to anyone.
    //
    public static string DescribePin(string? thumbprint) => string.IsNullOrWhiteSpace(thumbprint)
        ? Strings.ServerMap_NoPinRecorded
        : Format(Strings.ServerMap_PinRecordedFormat, Short(thumbprint));

    //
    // A 64-character hex string is unreadable and unusable for comparison. The ends are what people
    // actually check against each other, so those are what is shown.
    //
    public static string Short(string? thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint)) return Strings.ServerMap_NoThumbprint;

        var value = thumbprint.Trim();

        return value.Length <= 20 ? value : $"{value[..8]}...{value[^8..]}";
    }

    //
    // One sentence, plus the pin sentence when this is the connection that recorded it. Said once,
    // on that connection, so recording the certificate is something the user saw happen rather than
    // something that happened to them.
    //
    private static string Connected(ServerHelloProbe probe)
    {
        var name = string.IsNullOrWhiteSpace(probe.Hello!.ServerName)
            ? probe.Endpoint.Host
            : probe.Hello.ServerName!;

        var sentence = string.IsNullOrWhiteSpace(probe.Hello.SptVersion)
            ? Format(Strings.ServerMap_ConnectedFormat, name)
            : Format(Strings.ServerMap_ConnectedWithVersionFormat, name, probe.Hello.SptVersion);

        return probe.PinnedOnThisConnection
            ? sentence + " " + Format(Strings.ServerMap_FirstConnectionFormat, Short(probe.ActualThumbprint))
            : sentence;
    }

    private static string Format(string format, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, format, values);
}
