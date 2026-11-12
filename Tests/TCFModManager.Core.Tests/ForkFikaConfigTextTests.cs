using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// Fork (1.4.7): single values changed in fika.jsonc as text (FikaConfigText).
public class ForkFikaConfigTextTests
{
    // Shaped as Fika writes it (FikaConfigFile: client, server, natPunchServer, headless), with a comment.
    private const string File = """
        {
          // Fika's settings
          "client": {
            "useBtr": true,
            "friendlyFire": true,
            "useInertia": false,
            "reviveConfig": { "enabled": false }
          },
          "server": {
            "SPT": {
              "http": {
                "ip": "127.0.0.1",
                "port": 6969,
                "backendIp": "0.0.0.0"
              }
            }
          },
          "natPunchServer": { "enable": false, "ip": "127.0.0.1", "port": 6790 }
        }
        """;

    [Fact]
    public void A_switch_is_read_and_changed_and_nothing_else_moves()
    {
        Assert.True(FikaConfigText.ReadSwitch(File, "friendlyFire"));
        Assert.False(FikaConfigText.ReadSwitch(File, "useInertia"));
        Assert.Null(FikaConfigText.ReadSwitch(File, "allowFreeCam"));

        var changed = FikaConfigText.SetSwitch(File, "friendlyFire", false)!;

        Assert.Equal(File.Replace("\"friendlyFire\": true", "\"friendlyFire\": false"), changed);
        Assert.Contains("// Fika's settings", changed);
        Assert.Null(FikaConfigText.SetSwitch(File, "allowFreeCam", true));
    }

    [Fact]
    public void The_listen_address_is_the_servers_not_the_nat_punch_servers()
    {
        var changed = FikaConfigText.SetListenAddress(File, "0.0.0.0")!;

        Assert.Equal(File.Length - "127.0.0.1".Length + "0.0.0.0".Length, changed.Length);
        Assert.Contains("\"natPunchServer\": { \"enable\": false, \"ip\": \"127.0.0.1\"", changed);
        Assert.Equal("0.0.0.0", FikaServerConfig.Parse(changed)!.Ip);

        Assert.Null(FikaConfigText.SetListenAddress("{ \"server\": { \"SPT\": { \"http\": { \"port\": 1 } } }, \"x\": { \"ip\": \"1.2.3.4\" } }", "0.0.0.0"));
        Assert.Null(FikaConfigText.SetListenAddress("{}", "0.0.0.0"));
    }
}
