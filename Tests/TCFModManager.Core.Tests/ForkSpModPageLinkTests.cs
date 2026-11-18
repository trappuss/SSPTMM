using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// Fork (1.4.8): the page link pasted to tell SSPTMM which mod or addon a hand-installed folder is.
public sealed class ForkSpModPageLinkTests
{
    [Theory]
    [InlineData("https://sp-mod.com/mod/791/sain", 791, false)]
    [InlineData("  https://www.sp-mod.com/mod/791/sain#comments ", 791, false)]
    [InlineData("sp-mod.com/mod/2326", 2326, false)]
    [InlineData("http://sp-mod.com/mod/2326/", 2326, false)]
    [InlineData("https://sp-mod.com/addon/98/icebreaker-fika-sync?tab=versions", 98, true)]
    [InlineData("HTTPS://SP-MOD.COM/ADDON/98", 98, true)]
    public void A_page_link_names_its_mod_or_addon(string text, int id, bool isAddon)
    {
        Assert.True(SpModPageLink.TryParse(text, out var foundId, out var foundAddon));
        Assert.Equal(id, foundId);
        Assert.Equal(isAddon, foundAddon);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("791")]
    [InlineData("sain")]
    [InlineData("https://sp-mod.com/mods")]
    [InlineData("https://sp-mod.com/mod/sain")]
    [InlineData("https://sp-mod.com/mod/0/none")]
    [InlineData("https://sp-mod.com/list/12/a-list")]
    [InlineData("https://sp-mod.com/user/5/someone")]
    [InlineData("https://example.com/mod/791/sain")]
    [InlineData("https://sp-mod.com.example.com/mod/791")]
    [InlineData("https://sp-mod.com/mod/791/sain and more")]
    [InlineData("https://sp-mod.com/mod/791sain")]
    public void Anything_else_is_not_one(string? text)
    {
        Assert.False(SpModPageLink.TryParse(text, out var id, out var isAddon));
        Assert.Equal(0, id);
        Assert.False(isAddon);
    }
}
