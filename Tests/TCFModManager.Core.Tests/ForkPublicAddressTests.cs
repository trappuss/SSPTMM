using System.Net;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// Fork (1.4.8): this PC's public address for a Fika host's invite (PublicAddress).
public class ForkPublicAddressTests
{
    [Fact]
    public void Only_an_address_the_internet_can_route_is_an_answer()
    {
        Assert.Equal("203.0.113.7", PublicAddress.Read("203.0.113.7\n"));

        // A home network's, a provider's shared one, this PC itself, and what is no address.
        Assert.Null(PublicAddress.Read("192.168.1.5"));
        Assert.Null(PublicAddress.Read("100.72.1.9"));
        Assert.Null(PublicAddress.Read("127.0.0.1"));
        Assert.Null(PublicAddress.Read("2001:db8::1"));
        Assert.Null(PublicAddress.Read("12345"));
        Assert.Null(PublicAddress.Read("<html>Too many requests</html>"));
        Assert.Null(PublicAddress.Read(null));
    }

    [Fact]
    public async Task The_service_is_asked_once_and_sent_nothing()
    {
        var handler = new Service(HttpStatusCode.OK, "198.51.100.23");

        Assert.Equal("198.51.100.23", await PublicAddress.FindAsync(handler));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(PublicAddress.ServiceUrl, request.RequestUri!.GetLeftPart(UriPartial.Authority));
        Assert.Equal(string.Empty, request.RequestUri.Query);
        Assert.Null(request.Content);
    }

    [Fact]
    public async Task A_service_that_fails_gives_no_address()
    {
        Assert.Null(await PublicAddress.FindAsync(new Service(HttpStatusCode.TooManyRequests, "slow down")));
        Assert.Null(await PublicAddress.FindAsync(new Service(HttpStatusCode.OK, "not an address")));
        Assert.Null(await PublicAddress.FindAsync(new Service(null, string.Empty)));
    }

    [Fact]
    public void An_invite_with_the_public_address_is_read_back_with_it_last()
    {
        var invite = FikaInviteText.Parse(
            "Join my SPT server (Fika).\r\n"
            + "Address: 192.168.1.5:6969 (same network)\r\n"
            + "From outside my network: 203.0.113.7:6969\r\n"
            + "Versions: SPT 4.1.6, Fika 2.4.6");

        Assert.Equal(["192.168.1.5:6969", "203.0.113.7:6969"], invite.Addresses);
    }

    // Null status: the request itself fails.
    private sealed class Service(HttpStatusCode? status, string body) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (status is not { } code) throw new HttpRequestException("no route");

            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
        }
    }
}
