using System.Net.Http;

namespace TCFModManager.App.Services;

//
// Fork (SSPTMM 1.2.0): files.sp-mod.com serves pictures only to requests carrying a Referer from
// sp-mod.com, so pictures from there get one - and pictures from any other host (the image hosts a
// description links to) get none, rather than being told which site the reader came from.
//
internal sealed class SpModRefererHandler() : DelegatingHandler(new HttpClientHandler())
{
    private static readonly Uri Referrer = new("https://sp-mod.com/");

    public static bool IsSpMod(Uri? uri) =>
        uri is { Host: var host }
        && (host.Equals("sp-mod.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".sp-mod.com", StringComparison.OrdinalIgnoreCase));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Referrer = IsSpMod(request.RequestUri) ? Referrer : null;
        return base.SendAsync(request, cancellationToken);
    }
}
