using System.Globalization;
using System.Text.RegularExpressions;

namespace TCFModManager.Core.Services;

//
// Fork (1.4.8): which mod or addon a pasted sp-mod.com page link is for - sp-mod.com/mod/791/sain,
// sp-mod.com/addon/98/some-addon - with or without the scheme, the www, the name and anything after
// it. Mods and addons are numbered separately, so the kind is part of the answer.
//
public static partial class SpModPageLink
{
    public static bool TryParse(string? text, out int id, out bool isAddon)
    {
        id = 0;
        isAddon = false;

        var match = Page().Match(text?.Trim() ?? string.Empty);
        if (!match.Success
            || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out id)
            || id <= 0)
        {
            id = 0;
            return false;
        }

        isAddon = match.Groups[1].Value.Equals("addon", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    [GeneratedRegex(@"^(?:https?://)?(?:www\.)?sp-mod\.com/(mod|addon)/(\d{1,9})(?:[/?#]\S*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Page();
}
