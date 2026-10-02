using TCFModManager.App.Localization;

namespace TCFModManager.App.Services;

//
// What a check that someone asked for came to, in words - under the Options page's Check now, and
// in the notification the tray's "Check for updates now" answers with. One description, so the two
// never say different things about the same result.
//
internal static class UpdateCheckWording
{
    public static string Describe(UpdateCheckOutcome outcome) => outcome.Result switch
    {
        UpdateCheckResult.Checked when outcome.WasBaseline => Strings.Options_UpdateCheckBaseline,
        UpdateCheckResult.Checked when outcome.Announced > 0 =>
            Strings.Options_UpdateCheckAnnounced(outcome.Announced, outcome.Announced),
        UpdateCheckResult.Checked when outcome.Available > 0 =>
            Strings.Options_UpdateCheckNothingNew(outcome.Available, outcome.Available),
        UpdateCheckResult.Checked => Strings.Options_UpdateCheckNone,
        UpdateCheckResult.AlreadyChecking => Strings.Options_UpdateCheckAlreadyRunning,
        UpdateCheckResult.NoInstall => Strings.Options_UpdateCheckNoInstall,
        UpdateCheckResult.QueueBusy => Strings.Options_UpdateCheckQueueBusy,
        UpdateCheckResult.Offline => Strings.Options_UpdateCheckOffline,
        UpdateCheckResult.RateLimited => Strings.Options_UpdateCheckRateLimited,
        _ => Strings.Options_UpdateCheckFailed,
    };
}
