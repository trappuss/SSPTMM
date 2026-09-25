using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class SptServerReadinessTests
{
    [Fact]
    public void Reads_the_port_from_http_json_with_comments()
    {
        const string json = """
            {
                // SPT 4.x
                "ip": "127.0.0.1",
                "port": 7070,
                "backendIp": "127.0.0.1",
            }
            """;

        Assert.Equal(7070, SptServerReadiness.ReadPort(json));
    }

    [Fact]
    public void No_port_and_nonsense_give_nothing()
    {
        Assert.Null(SptServerReadiness.ReadPort("""{ "ip": "127.0.0.1" }"""));
        Assert.Null(SptServerReadiness.ReadPort("""{ "port": 99999 }"""));
        Assert.Equal(6970, SptServerReadiness.ReadPort("""{ "port": 6970 oops"""));
    }

    [Fact]
    public void The_default_port_is_always_watched_and_http_json_adds_its_own()
    {
        var root = Path.Combine(Path.GetTempPath(), "tcfmm-readiness-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "SPT_Data", "configs"));
            File.WriteAllText(Path.Combine(root, "SPT_Data", "configs", "http.json"), """{ "port": 7000 }""");

            var ports = SptServerReadiness.PortsFor(Path.Combine(root, "SPT.Server.exe"));

            Assert.Contains(6969, ports);
            Assert.Contains(7000, ports);
            Assert.Equal([6969], SptServerReadiness.PortsFor(null));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
