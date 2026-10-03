# Building from source

## The easy way

1. Get the code: **Code > Download ZIP** on GitHub, or `git clone https://github.com/trappuss/SSPTMM`.
2. Double-click **`SSPTMM-build-and-run.bat`**. It:
   1. finds a .NET 9 SDK, or installs one into `.dotnet\` beside the file (no admin rights);
   2. builds the app;
   3. runs the tests - a failure stops here;
   4. publishes a self-contained `SSPTMM.exe` into `dist\SSPTMM\`;
   5. starts it.

Everything it does is written to `logs\build.log`. `SSPTMM-test.bat` runs the tests on their own
(`logs\test.log`).

## By hand

```
dotnet build src\TCFModManager.App -c Release
dotnet test Tests\TCFModManager.Core.Tests -c Release
dotnet publish src\TCFModManager.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist\SSPTMM
```

## Layout

| Folder | What |
|---|---|
| `src\TCFModManager.Core` | Everything that isn't UI: sp-mod.com client, installing and removing, configs, logs, profiles. |
| `src\TCFModManager.App` | The WPF app (WPF-UI), its Steam theme, pages and strings (`Localization\Strings*.resx`). |
| `Tests\TCFModManager.Core.Tests` | xUnit tests for Core. |
| `docs\` | The development log (`steam-workshop-ui.md`), TCF Mod Manager's own README and guides, screenshots. |

The project and namespace names are still TCF Mod Manager's, which keeps merging newer TCF Mod
Manager releases simple. The version is set in one place: `build\Directory.Build.props`.

## Translations

The app's text is in `src\TCFModManager.App\Localization\Strings.resx`, with German, French,
Italian and Russian beside it. Steam's own terms use Steam's own translations; newer text falls back
to English until someone translates it - pull requests welcome.
