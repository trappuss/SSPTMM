using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// Fork (1.4.8): what the user is told after a copy to the headless client folder (HeadlessMirror) -
// on the Collections page, and under the folder's box in Options.
//
public static class HeadlessMirrorProblems
{
    private static string Text(string format, params object?[] values) => LocalizationService.Text(format, values);

    public static string Describe(HeadlessMirrorProblem problem, string folder) => problem switch
    {
        HeadlessMirrorProblem.NoFolder => Text(Strings.Headless_ProblemNoFolderFormat, folder),
        HeadlessMirrorProblem.NotAGame => Text(Strings.Headless_ProblemNotAGameFormat, folder),
        HeadlessMirrorProblem.SameAsInstall => Strings.Headless_ProblemSameAsInstall,
        HeadlessMirrorProblem.Link => Text(Strings.Headless_ProblemLinkFormat, folder),
        _ => Text(Strings.Headless_ProblemInUseFormat, folder),
    };

    public static string Describe(HeadlessMirrorResult result, string folder)
    {
        if (result.Problem is { } problem) return Describe(problem, folder);

        var message = Text(Strings.Headless_CopiedFormat, result.Copied.Count, result.Removed.Count, result.Unchanged.Count);
        return result.Failed.Count == 0
            ? message
            : message + " " + Text(Strings.Headless_FailedFormat, TextLists.Join([.. result.Failed.Select(f => f.Name).Distinct()]));
    }
}
