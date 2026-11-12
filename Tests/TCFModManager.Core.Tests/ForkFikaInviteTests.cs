using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// Fork (1.4.7): the invite a Fika host copies, read back on a friend's PC (FikaInviteText).
public class ForkFikaInviteTests
{
    [Fact]
    public void The_invite_the_app_writes_is_read_back()
    {
        var invite = FikaInviteText.Parse(
            "Join my SPT server (Fika).\r\n"
            + "Address: 192.168.1.5:6969 (same network) and 100.72.1.9:6969 (Tailscale)\r\n"
            + "From outside my network: my public address, port 6969.\r\n"
            + "Versions: SPT 4.1.6, Fika 2.4.6\r\n"
            + "With SSPTMM: on Play, \"Join from an invite\" and paste this message.");

        Assert.Equal(["192.168.1.5:6969", "100.72.1.9:6969"], invite.Addresses);
        Assert.Equal("4.1.6", invite.Spt);
        Assert.Equal("2.4.6", invite.Fika);
    }

    [Fact]
    public void Any_message_with_an_address_will_do_in_any_language()
    {
        // No port: SPT's own. A name needs a port or https:// to count.
        Assert.Equal(["203.0.113.7:6969"], FikaInviteText.Parse("komm rein: 203.0.113.7").Addresses);
        Assert.Equal(["fika.example.org:7000"], FikaInviteText.Parse("server is fika.example.org:7000, see sp-mod.com for mods").Addresses);
        Assert.Equal(["fika.example.org:6969"], FikaInviteText.Parse("https://fika.example.org").Addresses);

        var german = FikaInviteText.Parse("Adresse: 203.0.113.7:6969\nVersionen: SPT 4.1.6, Fika 2.4.6");
        Assert.Equal("4.1.6", german.Spt);
        Assert.Equal("2.4.6", german.Fika);
    }

    [Fact]
    public void Versions_this_pcs_own_addresses_and_plain_words_are_not_servers()
    {
        var invite = FikaInviteText.Parse("SPT 4.1.6 here, try 127.0.0.1:6969 or localhost. Mods at sp-mod.com. Twice: 203.0.113.7:6969, 203.0.113.7:6969");

        Assert.Equal(["203.0.113.7:6969"], invite.Addresses);
        Assert.Null(invite.Fika);
        Assert.Empty(FikaInviteText.Parse(null).Addresses);

        // A version of four numbers is still a version.
        var four = FikaInviteText.Parse("Versions: SPT 4.1.6.0, Fika 2.4.6.0 - 203.0.113.9");
        Assert.Equal(["203.0.113.9:6969"], four.Addresses);
        Assert.Equal("2.4.6.0", four.Fika);
    }
}
