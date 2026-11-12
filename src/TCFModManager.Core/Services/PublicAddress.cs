using System.Net;

namespace TCFModManager.Core.Services;

//
// Fork (1.4.8): this PC's address on the internet, for a Fika host's invite. Asked only with
// Options > Fika and servers > Public address in the invite on, and only when Copy invite is
// pressed. The service is sent nothing but the request and answers with the address as text.
//
public static class PublicAddress
{
    public const string ServiceUrl = "https://api.ipify.org";

    // Copy invite waits on this. Four seconds is HUNCH.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);

    /// <summary>The address, or null when the service can't be reached or answers with something else.</summary>
    public static async Task<string?> FindAsync(HttpMessageHandler? handler = null, CancellationToken ct = default)
    {
        using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.Timeout = Timeout;

        try
        {
            return Read(await http.GetStringAsync(ServiceUrl, ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    //
    // Only an IPv4 address the internet can route is an answer. A home network's or a shared one
    // (100.64.0.0/10, which internet providers use to put several customers behind one address)
    // can't be reached from outside, and an error page is not an address at all.
    //
    public static string? Read(string? text)
    {
        text = text?.Trim();
        if (text is not { Length: > 0 } || text.Count(c => c == '.') != 3 || !IPAddress.TryParse(text, out var ip)) return null;

        return LocalNetworkAddresses.Rank(ip) == 2 ? ip.ToString() : null;
    }
}
