using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace TCFModManager.App;

//
// Applies the stored ThemePreference, and follows Windows while the preference is Follow system.
//
// Everything theme-related lives here rather than being spread between App, MainWindow and the
// Options page: the three of them only say *when*, and the rules for what that means - including
// remembering to stop following the OS when the user pins a theme - stay in one place.
//
// The default preference is Follow system, so a fresh install matches the desktop. Every colour the
// app draws itself now comes from a WPF-UI theme brush rather than a literal, which is what makes
// Light a real option rather than an unreadable one - see Converters/ThemeBrush for the one case
// that can lag a live theme change.
//
public static class AppTheme
{
    // Stateless (it only holds a path), so constructing one costs nothing and every write does its
    // own Load first - the same load-mutate-save the rest of the app uses on this file.
    private static readonly SettingsService Settings = new();

    // The window currently handed to SystemThemeWatcher, so it can be un-watched again. Null when
    // the preference isn't Follow system.
    private static Window? _watching;

    // Guards against hooking ApplicationThemeManager.Changed more than once.
    private static bool _subscribed;

    // Guards against registering the Button class handler more than once.
    private static bool _buttonsHooked;

    // Which resting foregrounds have already been reported. Keyed on the description rather than
    // the brush so two equal colours from different themes only say it once.
    private static readonly HashSet<string> ReportedRestingForegrounds = [];

    //
    // How far the label dims while a button is held, as a multiplier on the resting brush's opacity.
    // The one number to turn if the press wants more or less weight; 1.0 holds the resting colour
    // exactly, which is where this started before the dim was asked for.
    //
    // Opacity rather than a darker colour, so it reads correctly in both themes without a second
    // value: a white label on a dark button dims towards the dark fill behind it, a near-black one
    // on a light button dims towards the light fill. Same gesture, opposite directions, which is
    // what WPF-UI's own pressed brushes were reaching for before the template bug below swallowed
    // them.
    //
    private const double PressedForegroundDim = 0.8;

    // Marks a button whose Foreground this class is holding for the duration of a press, so the
    // release only clears what it set. An attached property rather than a collection of buttons:
    // it lives and dies with the button, where a set would keep every button ever pressed alive.
    private static readonly DependencyProperty HoldingForegroundProperty =
        DependencyProperty.RegisterAttached(
            "HoldingForeground", typeof(bool), typeof(AppTheme), new PropertyMetadata(false));

    public static ThemePreference Stored => Settings.Load().Theme;

    //
    // Applies the stored preference. Called from App.OnStartup, before the main window exists, so
    // the window is painted in the right theme rather than being repainted a moment after it opens.
    //
    public static void ApplyStored() => ApplyOnly(Stored);

    //
    // Hooks the main window up: repaints its chrome on every theme change, and starts following
    // Windows if that's what the preference says. Separate from ApplyStored because both of those
    // need a real window, which doesn't exist yet at startup.
    //
    public static void Attach(Window window)
    {
        if (!_subscribed)
        {
            // Subscribed rather than only called from ApplyOnly, so a theme change that SystemThemeWatcher
            // makes on its own - the user changing Windows' theme while Follow system is on - repaints
            // the chrome too.
            ApplicationThemeManager.Changed += (_, _) =>
            {
                RefreshWindowChrome();
                PinPressedButtonForeground();
            };
            _subscribed = true;
        }

        // Never follows Windows here: a watcher left running would put WPF-UI's light theme back
        // the moment Windows switched to light, which is exactly what the Steam look must not do.
        StopFollowing();
        _ = window;
    }

    // Applies a newly chosen preference, saves it, and starts or stops following Windows to match.
    public static void Set(ThemePreference preference)
    {
        ApplyOnly(preference);

        var settings = Settings.Load();
        settings.Theme = preference;
        Settings.Save(settings);

        // See ApplyOnly: the Steam look does not follow Windows, whatever the stored preference.
        StopFollowing();

        AppLog.Info("Theme", $"set to {preference}");
    }

    //
    // The Steam Workshop fork has one look, the way Steam does: WPF-UI's dark theme with the Steam
    // palette laid over it (Themes/SteamTheme.xaml) and Steam's blue as the accent. The stored
    // preference is still read and still saved, so going back to the upstream app finds the
    // setting where it was left - it just no longer changes what is drawn here, and the Options
    // page no longer offers it.
    //
    // No Mica backdrop: Steam's pages are flat colour, and Mica would tint them with the desktop
    // wallpaper. updateAccent is false because the accent WPF-UI would pick is the Windows one.
    //
    private static void ApplyOnly(ThemePreference preference)
    {
        _ = preference;

        ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.None, updateAccent: false);
        ApplySteamAccent();

        RefreshWindowChrome();
        PinPressedButtonForeground();
        HookButtonPressFeedback();
    }

    // Steam's new-UI blue (#1A9FFF) for the accent and its legacy link blue (#66C0F4) as the
    // lighter tertiary shade, matching the literal accent colours in Themes/SteamTheme.xaml so a
    // DynamicResource lookup and a StaticResource one inside that file agree.
    private static void ApplySteamAccent()
    {
        var blue = Color.FromRgb(0x1A, 0x9F, 0xFF);
        var legacyBlue = Color.FromRgb(0x66, 0xC0, 0xF4);

        ApplicationAccentColorManager.Apply(blue, blue, blue, legacyBlue);
    }

    //
    // Makes a pressed button read as the button changing colour, not the label changing - for the
    // plain WPF Button and RepeatButton. WPF-UI's own ui:Button needs a different lever and is
    // handled by HookButtonPressFeedback below.
    //
    // Those two templates paint the pressed label straight from the ButtonForegroundPressed brush,
    // which in dark mode is the resting white dropped to 77% alpha, and in light mode black at 62%.
    // The background and border already carry the feedback perfectly well on their own, so pointing
    // that brush at the resting ButtonForeground neutralises the label without losing anything.
    //
    // Deliberately a brush override rather than a Style: an implicit Style TargetType="ui:Button"
    // in App.xaml was tried first and stripped every button in the app of its chrome. WPF-UI
    // declares the real style in a theme dictionary that ApplicationThemeManager merges at RUNTIME,
    // so a BasedOn="{StaticResource {x:Type ui:Button}}" resolved when App.xaml is parsed never
    // reaches it - and the implicit style then shadows the real one with no template behind it.
    // Overriding a brush can at worst give a wrong colour; it cannot take a template away.
    //
    // Has to run after every Apply, not once at startup: each theme swaps in a fresh
    // ButtonForeground, and this needs to track it. An entry set directly on Application.Resources
    // is found ahead of any merged dictionary, which is what makes the override win.
    //
    // Buttons that set their own Foreground in XAML - Remove, Sort out, the folder links - were
    // never affected either way: a local value already outranks a template trigger.
    //
    private static void PinPressedButtonForeground()
    {
        if (Application.Current is not { } app) return;

        var resting = app.TryFindResource("ButtonForeground") as Brush;
        var before = app.TryFindResource("ButtonForegroundPressed") as Brush;

        // Guarded rather than assumed. If WPF-UI ever renames the key, doing nothing leaves the
        // momentary colour flip; writing null would make every button label disappear.
        if (resting is not null) app.Resources["ButtonForegroundPressed"] = resting;

        // Logged because this is invisible when it silently does nothing - a missing key, or an
        // override that doesn't win the lookup, both look exactly like "no effect" from outside.
        //
        // Only when it moves, which also swallows the duplicate: a theme the user picks comes
        // through ApplyOnly and again through ApplicationThemeManager.Changed, and the second pass
        // has nothing left to change.
        var after = app.TryFindResource("ButtonForegroundPressed") as Brush;
        if (!ReferenceEquals(before, after))
        {
            AppLog.Debug("Theme",
                $"pressed button foreground: resting={Describe(resting)}, was={Describe(before)}, "
                + $"now={Describe(after)}");
        }
    }

    //
    // The same thing for WPF-UI's own Button, which needs a different lever entirely.
    //
    // The label of a held ui:Button turns opaque black - #000000 measured off a dark-mode
    // screenshot, in a theme with nothing black in it. The cause is a bug in WPF-UI's own template,
    // in the pressed MultiTrigger:
    //
    //     <Setter Property="Foreground"
    //             Value="{Binding PressedForeground,
    //                             RelativeSource={RelativeSource TemplatedParent}}" />
    //
    // With no TargetName that Setter targets the templated parent - the button itself - so the
    // binding is evaluated against the button, whose own TemplatedParent is null for any button
    // that is not itself inside someone else's template. The binding therefore has no source,
    // resolves to nothing, and Foreground falls back to its registered default, which is
    // SystemColors.ControlTextBrush: the Win32 button-text colour, black in either theme.
    //
    // So PressedForeground is a red herring. Two earlier fixes assigned it - via the theme brush,
    // then per instance at mouse-down - and the log confirmed both landed while the label went on
    // rendering black, because that trigger never successfully reads the property it names.
    //
    // What does work is outranking the trigger. A local value sits above template triggers in WPF's
    // precedence order, so the button's own resting brush - dimmed by PressedForegroundDim - is
    // written onto Foreground as the press begins and cleared again when it ends: the trigger still
    // fires, still loses, and the label dims by the amount this file chooses instead of dropping to
    // black. The background and border carry most of the press, as they always did; the label just
    // goes along with them.
    //
    // Read at mouse-down rather than resolved from a key, which is what keeps it right for free.
    // PreviewMouseLeftButtonDown tunnels before ButtonBase sets IsPressed, so Foreground still
    // holds the resting value - including whatever an Appearance trigger put there, and whichever
    // theme is current. Cleared on release rather than left in place, so a theme change or an
    // Appearance change between presses is picked up by the next one.
    //
    // Class handlers rather than per-instance subscriptions: registered once for the type, so
    // nothing holds a reference to individual buttons - which matters here, where the card and
    // list views build and discard them constantly.
    //
    private static void HookButtonPressFeedback()
    {
        if (_buttonsHooked) return;
        _buttonsHooked = true;

        // handledEventsToo throughout, because a button sitting on something that handles the press
        // itself - a clickable card row - would otherwise never be seen.
        EventManager.RegisterClassHandler(
            typeof(Wpf.Ui.Controls.Button),
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler((sender, _) => HoldRestingForeground(sender)),
            true);

        // Space and Enter press a focused button too, and go through the same trigger.
        EventManager.RegisterClassHandler(
            typeof(Wpf.Ui.Controls.Button),
            UIElement.PreviewKeyDownEvent,
            new KeyEventHandler(OnButtonKeyDown),
            true);

        // Losing the mouse capture is the reliable end of a press: ButtonBase takes the capture on
        // the way down and gives it back on the way up, including when the pointer is dragged off
        // the button and released somewhere else, which PreviewMouseLeftButtonUp would miss.
        EventManager.RegisterClassHandler(
            typeof(Wpf.Ui.Controls.Button),
            UIElement.LostMouseCaptureEvent,
            new MouseEventHandler((sender, _) => ReleaseRestingForeground(sender)),
            true);

        EventManager.RegisterClassHandler(
            typeof(Wpf.Ui.Controls.Button),
            UIElement.PreviewKeyUpEvent,
            new KeyEventHandler(OnButtonKeyUp),
            true);
    }

    private static void OnButtonKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Space or Key.Enter) HoldRestingForeground(sender);
    }

    private static void OnButtonKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Space or Key.Enter) ReleaseRestingForeground(sender);
    }

    private static void HoldRestingForeground(object sender)
    {
        if (sender is not Wpf.Ui.Controls.Button button) return;
        if ((bool)button.GetValue(HoldingForegroundProperty)) return;

        // Already carries a Foreground of its own - Remove, Sort out, the folder links. That is the
        // same local value this would be adding, so those buttons already outrank the trigger and
        // there is nothing to do; touching them would mean clearing something this did not set.
        if (button.ReadLocalValue(Wpf.Ui.Controls.Button.ForegroundProperty)
            != DependencyProperty.UnsetValue)
        {
            return;
        }

        // Nothing to preserve, and writing null would make the label vanish rather than hold.
        if (button.Foreground is not { } resting) return;

        // Clone rather than rebuild from the colour: Opacity is a Brush property, so this dims a
        // gradient or an image brush exactly as well as the SolidColorBrush every button actually
        // has, and needs no special case for either. Frozen because it is written once and only
        // read after that - and because an unfrozen brush assigned to a control keeps a change
        // subscription alive that this has no use for.
        var held = resting.Clone();
        held.Opacity = resting.Opacity * PressedForegroundDim;
        held.Freeze();

        button.SetValue(HoldingForegroundProperty, true);
        button.Foreground = held;

        // Once per distinct colour, not once per press. Describes what was held rather than what
        // was read, so the line shows the dim actually applied - Describe folds Opacity into the
        // alpha it prints.
        if (ReportedRestingForegrounds.Add(Describe(held)))
        {
            AppLog.Debug("Theme", $"holding {Describe(held)} through the press");
        }
    }

    private static void ReleaseRestingForeground(object sender)
    {
        if (sender is not Wpf.Ui.Controls.Button button) return;
        if (!(bool)button.GetValue(HoldingForegroundProperty)) return;

        button.SetValue(HoldingForegroundProperty, false);

        // Back to the style and its Appearance triggers, so the next press re-reads whatever the
        // theme and the button's appearance say by then.
        button.ClearValue(Wpf.Ui.Controls.Button.ForegroundProperty);
    }

    //
    // rgba() rather than the #AARRGGBB that Color.ToString gives, because the alpha is the whole
    // point in this file and reading it out of the leading two hex digits is a chore. The wrapper
    // earns its keep: four bare numbers in a log line are just four numbers.
    //
    // The brush's own Opacity folds into the alpha - a theme brush carries its alpha in the colour,
    // but the ones this app declares (the row tints) set Opacity instead, and either way what
    // matters is what lands on screen.
    //
    private static string Describe(Brush? brush) => brush switch
    {
        null => "missing",
        SolidColorBrush solid =>
            $"rgba({solid.Color.R}, {solid.Color.G}, {solid.Color.B}, "
            + $"{solid.Color.A / 255d * solid.Opacity:0.##})",
        _ => brush.GetType().Name,
    };

    //
    // Repaints the window's own chrome - the title bar, the backdrop behind the navigation rail and
    // page area.
    //
    // Applying a theme swaps the brushes in the application's resource dictionaries, which is enough
    // for everything drawn *inside* the window (cards, text, controls all re-resolve immediately).
    // The window frame is not drawn from those brushes: FluentWindow sets its backdrop and the Win32
    // dark-mode title bar attribute once when it is created, and nothing revisits them afterwards.
    // Without this, switching theme with the app open left a light page sitting inside a dark frame -
    // while launching with the same theme already stored looked perfectly correct, because the window
    // was built after the theme was applied.
    //
    private static void RefreshWindowChrome()
    {
        // Absent at startup, when ApplyStored runs before any window exists. Nothing to do then:
        // FluentWindow reads the current theme itself as it is created.
        if (Application.Current?.MainWindow is not FluentWindow window) return;

        try
        {
            WindowBackgroundManager.UpdateBackground(
                window,
                ApplicationThemeManager.GetAppTheme(),
                window.WindowBackdropType);
        }
        catch (Exception ex)
        {
            // Chrome that didn't repaint is ugly, not broken, and is not worth taking the app down for.
            AppLog.Warn("Theme", $"couldn't repaint the window chrome: {ex.Message}");
        }
    }

    private static void StartFollowing(Window window)
    {
        if (ReferenceEquals(_watching, window)) return;

        StopFollowing();
        SystemThemeWatcher.Watch(window);
        _watching = window;
    }

    private static void StopFollowing()
    {
        if (_watching is null) return;

        SystemThemeWatcher.UnWatch(_watching);
        _watching = null;
    }
}
