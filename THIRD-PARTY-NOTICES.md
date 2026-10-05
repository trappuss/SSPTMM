# Third-party notices

SSPTMM's own code is under the [MIT License](LICENSE). The release build (`SSPTMM.exe`) also
contains the components below. Their licence texts ship in the `Licenses\` folder beside the exe;
in this repository they are in `build/steam-ui/licenses/` (and the font licence in
`src/TCFModManager.App/Themes/Fonts/OFL.txt`).

| Component | Licence | Copyright | File in `Licenses\` |
|---|---|---|---|
| TCF Mod Manager (what SSPTMM is built on) | MIT, as listed on [sp-mod.com](https://sp-mod.com/mod/2945/tcf-mod-manager) | TheCrimsonFckr | `LICENSE.txt` beside the exe |
| .NET runtime and Windows Desktop runtime (WPF), bundled because the exe is self-contained; also System.Drawing.Common, Microsoft.Win32.SystemEvents, Microsoft.Win32.Registry, System.Security.AccessControl, System.Security.Principal.Windows, System.Reflection.Emit, System.ValueTuple | MIT | .NET Foundation and Contributors | `DotNet-Runtime-LICENSE.txt`, `DotNet-Runtime-ThirdPartyNotices.txt` |
| Windows SDK .NET projection 10.0.17763 (`Microsoft.Windows.SDK.NET.dll`), from the Windows 10 1809 target the notifications need | Microsoft Windows SDK licence terms (Distributable Code) | Microsoft Corporation | `WindowsSDK-LICENSE.txt` |
| C#/WinRT runtime (`WinRT.Runtime.dll`), shipped with the projection above | MIT | Microsoft Corporation | `CsWinRT-LICENSE.txt` |
| WPF UI, WPF UI Tray, WPF UI Abstractions 4.3.0 | MIT | Leszek Pomianowski and WPF UI Contributors | `WPF-UI-LICENSE.txt`, `WPF-UI-ThirdPartyNotices.txt` |
| CommunityToolkit.Mvvm 8.4.2 | MIT | .NET Foundation and Contributors | `CommunityToolkit.Mvvm-LICENSE.txt`, `CommunityToolkit.Mvvm-ThirdPartyNotices.txt` |
| Microsoft.Toolkit.Uwp.Notifications 7.1.3 | MIT | .NET Foundation and Contributors | `Microsoft.Toolkit.Uwp.Notifications-LICENSE.txt` |
| SharpCompress 0.50.4 | MIT | Adam Hathcock | `SharpCompress-LICENSE.txt` |
| HtmlAgilityPack 1.13.0 | MIT | ZZZ Projects Inc. | `HtmlAgilityPack-LICENSE.txt` |
| SharpHDiffPatch.Core 2.3.0 (applies SPT's game patches for the experimental Play) | MIT | Kemal Setya Adhi (neon-nyan), Collapse Project Team | `SharpHDiffPatch-LICENSE.txt` |
| HDiffPatch, which SharpHDiffPatch ports (includes libdivsufsort's notice) | MIT | housisong; Yuta Mori | `HDiffPatch-LICENSE.txt` |
| ZstdSharp.Port 0.8.5, a C# port of Zstandard | MIT | Oleg Stepanischev | `ZstdSharp-LICENSE.txt` |
| Zstandard, which ZstdSharp ports | BSD | Meta Platforms, Inc. and affiliates | `Zstandard-LICENSE.txt` |
| Hi3Helper.ZstdNet 1.6.4 (managed part only; its native `libzstd.dll` is not shipped) | BSD 3-Clause | SKB Kontur, Collapse Project Team | `ZstdNet-LICENSE.txt` |
| XamlAnimatedGif 2.3.2 | Apache 2.0 | Thomas Levesque | `XamlAnimatedGif-LICENSE.txt` |
| Microsoft Edge WebView2 SDK 1.0.4191.47 | Microsoft's WebView2 licence (BSD-style) | Microsoft Corporation | `WebView2-LICENSE.txt` |
| Noto Sans (the app's font) | SIL Open Font License 1.1 | The Noto Project Authors | `NotoSans-OFL.txt` |

Steam and the Steam Workshop are trademarks of Valve Corporation, and Escape from Tarkov is a
trademark of Battlestate Games. SSPTMM is an independent fan project, not affiliated with or
endorsed by either; the licences above grant nothing about those names.
