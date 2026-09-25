"""Regenerates src/TCFModManager.App/Themes/SteamTheme.xaml from WPF UI's own Dark.xaml.

Usage:  python gen_steam_theme.py <path to WPF UI 4.3.0 source>/src/Wpf.Ui/Resources/Theme/Dark.xaml
        (git clone --depth 1 --branch 4.3.0 https://github.com/lepoco/wpfui)
Run from the repo root. See docs/steam-workshop-ui.md for where the colours come from.
"""
import os, re, sys
if len(sys.argv) != 2:
    sys.exit(__doc__)
src = open(sys.argv[1], encoding='utf-8').read()

# Steam values, measured from steamcommunity.com on 2026-09-24 (see STEAM_SPEC.md), keyed by
# WPF-UI Dark.xaml Color key. Anything not listed keeps WPF-UI's own dark value.
colors = {
    'ApplicationBackgroundColor': '#FF1B2838',        # legacy item page body
    'TextFillColorPrimary': '#DFE3E6',                # search input text
    'TextFillColorSecondary': '#ACB2B8',              # body text / labels (new UI)
    'TextFillColorTertiary': '#8F98A0',               # breadcrumbs, stat labels
    'TextFillColorDisabled': '#61686D',               # minor text
    'TextPlaceholderColor': '#8F98A0',
    'TextOnAccentFillColorSelectedText': '#FFFFFF',
    'TextOnAccentFillColorPrimary': '#FFFFFF',        # white text on #1A9FFF buttons
    'TextOnAccentFillColorSecondary': '#C0FFFFFF',
    'TextOnAccentFillColorDisabled': '#87FFFFFF',
    'ControlFillColorDefault': '#3D4450',             # gray button / dropdown
    'ControlFillColorSecondary': '#67707B',           # gray button hover (measured)
    'ControlFillColorTertiary': '#323843',
    'ControlFillColorDisabled': '#2A2F38',
    'ControlFillColorInputActive': '#1C2025',         # search input field
    'ControlSolidFillColorDefault': '#3D4450',
    'ControlAltFillColorSecondary': '#33FFFFFF',      # tag box at rest (white 20%)
    'ControlAltFillColorTertiary': '#3B3D40',         # tag box hover (measured)
    'ControlAltFillColorQuarternary': '#4A4D52',
    'ControlStrokeColorDefault': '#0AFFFFFF',         # Steam controls are borderless
    'ControlStrokeColorSecondary': '#00FFFFFF',
    'CardStrokeColorDefault': '#00000000',
    'CardStrokeColorDefaultSolid': '#171D25',
    'SurfaceStrokeColorDefault': '#3D4450',
    'CardBackgroundFillColorDefault': '#33000000',    # Steam panel: black 20%
    'CardBackgroundFillColorSecondary': '#1F000000',
    'SmokeFillColorDefault': '#99000000',
    'LayerFillColorDefault': '#00000000',             # let the window background show
    'LayerOnMicaBaseAltFillColorDefault': '#00000000',
    'AcrylicBackgroundFillColorDefault': '#21262E',   # sort panel / dropdown bg
    'SolidBackgroundFillColorBase': '#1B2838',
    'SolidBackgroundFillColorSecondary': '#171D25',
    'SolidBackgroundFillColorTertiary': '#213043',
    'SolidBackgroundFillColorQuarternary': '#2A3F5A',
    'SolidBackgroundFillColorBaseAlt': '#0E141B',
    'SystemFillColorAttention': '#1A9FFF',
    'SystemFillColorSuccess': '#A4D007',              # Steam subscribe green (top stop)
    'SystemFillColorCaution': '#D3AC42',              # Steam gold (measured, exclude tag)
    'SystemFillColorCritical': '#E05A5A',             # HUNCH: not measured on Workshop pages
    'SystemFillColorSuccessBackground': '#2A3A12',
    'SystemFillColorCautionBackground': '#3B3420',
    'SystemFillColorCriticalBackground': '#3E2226',
    'SystemFillColorSolidAttentionBackground': '#213043',
    'SystemFillColorSolidNeutralBackground': '#213043',
}

used = set()
def repl(m):
    key, val = m.group(1), m.group(2)
    if key in colors:
        used.add(key)
        return f'<Color x:Key="{key}">{colors[key]}</Color>'
    return m.group(0)

out = re.sub(r'<Color x:Key="([^"]+)">([^<]+)</Color>', repl, src)
missing = set(colors) - used
if missing:
    sys.exit(f'keys not found in Dark.xaml: {missing}')

# Accent colours defined locally, so StaticResource references to them inside this dictionary
# resolve here rather than depending on ApplicationAccentColorManager having run first.
accent = '''
    <!--  Steam accent (#1A9FFF new UI blue, #66C0F4 legacy link blue). Also applied through
          ApplicationAccentColorManager by AppTheme, so DynamicResource lookups agree.  -->
    <Color x:Key="SystemAccentColor">#1A9FFF</Color>
    <Color x:Key="SystemAccentColorPrimary">#1A9FFF</Color>
    <Color x:Key="SystemAccentColorSecondary">#1A9FFF</Color>
    <Color x:Key="SystemAccentColorTertiary">#66C0F4</Color>
    <Color x:Key="AccentFillColorDefault">#1A9FFF</Color>
    <Color x:Key="AccentFillColorSecondary">#56ABFF</Color>
    <Color x:Key="AccentFillColorTertiary">#1580D0</Color>
'''
out = out.replace('<Color x:Key="ApplicationBackgroundColor">', accent + '\n    <Color x:Key="ApplicationBackgroundColor">', 1)

# Individual brush tweaks where Steam differs from the colour the brush borrows.
tweaks = {
    'CheckBoxBorderBrush': 'Color="#00000000"',                 # tag boxes have no stroke
    'ToolTipBackground': 'Color="#344352"',                     # workshop hover popup (measured)
    'ToolTipBorderBrush': 'Color="#00000000"',
    'ToolTipForeground': 'Color="#ACB2B8"',
    'ComboBoxDropDownBorderBrush': 'Color="#00000000"',
    'ContentDialogBorderBrush': 'Color="#00000000"',
    'HyperlinkButtonForeground': 'Color="#1A9FFF"',
    'HyperlinkButtonForegroundPointerOver': 'Color="#66C0F4"',
}
for key, value in tweaks.items():
    pat = re.compile(r'(<SolidColorBrush x:Key="%s") Color="\{[^}]+\}"' % key)
    out, n = pat.subn(r'\1 ' + value, out)
    if n != 1: sys.exit(f'tweak {key} matched {n}')

header = '''<!--
    Steam Workshop palette for TCFModManager.

    A copy of WPF UI 4.3.0's Resources/Theme/Dark.xaml with its colour values replaced by the ones
    measured from steamcommunity.com (Workshop browse and item pages), so every brush key WPF UI's
    controls ask for is present and consistent. It is merged AFTER ui:ThemesDictionary in App.xaml,
    which makes it win the lookup; ApplicationThemeManager swaps the theme dictionary in place, so
    this one stays after it. Regenerate from the WPF UI source rather than editing brushes by hand -
    the brushes read their colours with StaticResource, so a brush and its colour must live in the
    same dictionary to agree.

    Original file:
-->
'''
out = header + out
open(os.path.join('src', 'TCFModManager.App', 'Themes', 'SteamTheme.xaml'), 'w', encoding='utf-8', newline='\n').write(out.replace('\r\n','\n'))
print('ok', len(out))
