using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// Fork (1.4.7): whether Windows Firewall lets a friend reach a Fika host's server (FirewallRules).
public class ForkFirewallRulesTests
{
    private const string Server = @"C:\SPT\SPT_Runtime\SPT.Server.exe";
    private const int Private = 2;

    private static FirewallRule Rule(string? app, int protocol, string? ports, bool allows = true, bool enabled = true, bool inbound = true, int profiles = Private) =>
        new(app, protocol, ports, enabled, allows, inbound, profiles);

    private static bool? Reaches(params FirewallRule[] rules) =>
        FirewallRules.Reaches(new FirewallState(rules, Private, On: true), Server, FirewallRules.Tcp, 6969);

    [Fact]
    public void With_no_rule_nothing_gets_in()
    {
        Assert.False(Reaches());
        Assert.False(Reaches(Rule(@"C:\Other\app.exe", FirewallRules.Tcp, "*")));
        Assert.False(Reaches(Rule(Server, FirewallRules.Udp, "*")));
        Assert.False(Reaches(Rule(Server, FirewallRules.Tcp, "80,443")));
        Assert.False(Reaches(Rule(Server, FirewallRules.Tcp, "*", enabled: false)));
        Assert.False(Reaches(Rule(Server, FirewallRules.Tcp, "*", inbound: false)));
        Assert.False(Reaches(Rule(Server, FirewallRules.Tcp, "*", profiles: 4))); // public only; the PC is on a private network
    }

    [Fact]
    public void The_rule_windows_makes_when_asked_lets_it_in()
    {
        // As "Allow access" writes it: the program, any port, one rule each for TCP and UDP.
        Assert.True(Reaches(Rule(@"c:\spt\spt_runtime\spt.server.exe", FirewallRules.Tcp, "*")));
        Assert.True(Reaches(Rule(Server, 256, null)));
        Assert.True(Reaches(Rule(null, FirewallRules.Tcp, "6969"))); // a port opened by hand
        Assert.True(Reaches(Rule(Server, FirewallRules.Tcp, "80, 6900-7000")));
        Assert.True(Reaches(Rule(Server, FirewallRules.Tcp, "*", profiles: int.MaxValue)));
    }

    [Fact]
    public void A_rule_that_blocks_wins()
    {
        // What refusing the question leaves behind.
        Assert.False(Reaches(Rule(Server, FirewallRules.Tcp, "*"), Rule(Server, FirewallRules.Tcp, "*", allows: false)));
    }

    [Fact]
    public void A_rule_for_no_program_counts_only_when_it_opens_the_port_by_name()
    {
        // As Windows lists a Store app's rule, and a single account's block - neither is about this program.
        Assert.False(Reaches(Rule(null, 256, null)));
        Assert.False(Reaches(Rule(null, FirewallRules.Tcp, "*")));
        Assert.True(Reaches(Rule(Server, FirewallRules.Tcp, "*"), Rule(null, 256, null, allows: false)));
        Assert.True(Reaches(Rule(Server, FirewallRules.Tcp, "*"), Rule(null, FirewallRules.Tcp, "6969", allows: false)));
    }

    [Fact]
    public void A_firewall_that_is_off_or_unread_is_not_called_closed()
    {
        Assert.True(FirewallRules.Reaches(new FirewallState([], Private, On: false), Server, FirewallRules.Tcp, 6969));
        Assert.Null(FirewallRules.Reaches(null, Server, FirewallRules.Tcp, 6969));
    }

    [Fact]
    public void Reading_this_pcs_firewall_does_not_throw()
    {
        var state = FirewallRules.Read();
        if (state is not null) Assert.True(state.CurrentProfiles >= 0);
    }
}
