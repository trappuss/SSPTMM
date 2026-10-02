@echo off
setlocal EnableExtensions
rem ---------------------------------------------------------------------------------------------
rem  SSPTMM - measure how smoothly the app scrolls on this PC. One double-click.
rem
rem   1. Closes the build in dist\steam-ui if it is open (any other copy of the app is left alone)
rem      and starts it again with its measuring switched on (TCFMM_PERF=1).
rem   2. You scroll a few pages, as this window says, then close the app.
rem   3. Reads what the app logged and writes "Claude outputs\perf-report.txt": frames per second
rem      and the longest stall while scrolling, page by page; how long each description took to
rem      show; the graphics card and screen. A short summary is shown here too.
rem
rem  Nothing is changed or sent anywhere. Build the app first with steam-ui-rebuild-and-run.bat.
rem ---------------------------------------------------------------------------------------------

pushd "%~dp0" || exit /b 1
set "ROOT=%~dp0"
set "OUT=%ROOT%dist\steam-ui"
set "REPORT=%ROOT%Claude outputs\perf-report.txt"
set "SELF=%~f0"

if not exist "%OUT%\SSPTMM.exe" (
    echo dist\steam-ui\SSPTMM.exe is not there yet. Run steam-ui-rebuild-and-run.bat first,
    echo close the app it opens, then run this again.
    goto :end
)

echo [1/3] Closing dist\steam-ui\SSPTMM.exe if it is open...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-Process SSPTMM -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $env:OUT + '\SSPTMM.exe' } | Stop-Process -Force; Start-Sleep -Seconds 1"

rem Only log lines written from now on count: the time as digits, compared with the log line's own.
set "START="
for /f "delims=" %%t in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMddHHmmss"') do set "START=%%t"

echo.
echo [2/3] Starting the app with measuring on. Please, with the window at the size you normally use:
echo.
echo       a. Browse the Workshop: scroll the item grid down and back up for about 10 seconds.
echo       b. Open SAIN (or any item with a long description) and scroll its page top to bottom
echo          and back for about 10 seconds.
echo       c. Close that item, open one with a short description, and scroll it the same way.
echo       d. Close the app. The report is written as soon as it closes.
echo.
set "TCFMM_PERF=1"
start "" /wait "%OUT%\SSPTMM.exe"
set "TCFMM_PERF="

echo [3/3] Reading the log...
if not exist "%ROOT%Claude outputs" mkdir "%ROOT%Claude outputs"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$t = [IO.File]::ReadAllText($env:SELF); $m = '#' + '#PS'; Invoke-Expression ('& {' + [char]10 + $t.Substring($t.IndexOf($m)) + [char]10 + '}')"

:end
echo.
pause
popd
exit /b 0

##PS - everything below is PowerShell, run by the line above; cmd never reaches it.
$ErrorActionPreference = 'Stop'
$logs = Join-Path $env:OUT 'Data\logs'
$lines = @()
if (Test-Path $logs) {
    Get-ChildItem $logs -Filter 'ssptmm-*.log' | Sort-Object Name | Select-Object -Last 2 | ForEach-Object {
        $lines += Get-Content -LiteralPath $_.FullName -Encoding UTF8
    }
}

# This run's [Perf] lines: written since the app was started from here.
$perf = @($lines | Where-Object { $_ -match '\[Perf\]' -and $_.Length -ge 19 -and ($_.Substring(0, 19) -replace '\D', '') -ge $env:START })
if (-not ($perf | Where-Object { $_ -match 'measuring - render tier' })) {
    Write-Host ''
    Write-Host 'The app did not record anything - it may not have started, or it is an older build without'
    Write-Host 'measuring. Run steam-ui-rebuild-and-run.bat, close the app, then run this again.'
    return
}

$tierLine = $perf | Select-String 'render tier (\d)' | Select-Object -Last 1
$tier = [int]$tierLine.Matches[0].Groups[1].Value

# Page by page: what was on screen during each scrolling second.
$place = 'start'
$back = 'start'
$segments = [ordered]@{}
foreach ($l in $perf) {
    $text = $l -replace '^.*\[Perf\]\s*', ''
    if ($text -match '^page (\S+)') { $place = $Matches[1]; $back = $place; continue }
    if ($text -eq 'item page opened') { $place = 'item page'; continue }
    if ($text -eq 'item page closed') { $place = $back; continue }
    if ($text -match '^description (\d+) chars') {
        # The item's own description is the first one its page shows.
        if ($place -eq 'item page') { $place = "item page ($($Matches[1]) chars)" }
        continue
    }
    if ($text -match '^scrolling: fps ([\d.]+), worst (\d+)ms') {
        if (-not $segments.Contains($place)) { $segments[$place] = New-Object System.Collections.ArrayList }
        [void]$segments[$place].Add([pscustomobject]@{ Fps = [double]$Matches[1]; Worst = [int]$Matches[2] })
    }
}

function Median($values) {
    $s = @($values | Sort-Object)
    if ($s.Count -eq 0) { return 0 }
    return $s[[int][Math]::Floor(($s.Count - 1) / 2)]
}

$gpu = @(); $cpu = '?'; $os = '?'
try {
    $gpu = @(Get-CimInstance Win32_VideoController)
    $cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name
    $os = (Get-CimInstance Win32_OperatingSystem).Caption
} catch { }
$refresh = ($gpu | Where-Object { $_.CurrentRefreshRate } | Select-Object -First 1).CurrentRefreshRate

$out = New-Object System.Collections.Generic.List[string]
$out.Add("SSPTMM - scrolling measurement, $(Get-Date -Format 'yyyy-MM-dd HH:mm')")
$out.Add('')
$out.Add("Windows:   $os")
$out.Add("Processor: $cpu")
foreach ($g in $gpu) {
    $out.Add("Graphics:  $($g.Name) (driver $($g.DriverVersion)), $($g.CurrentHorizontalResolution)x$($g.CurrentVerticalResolution) at $($g.CurrentRefreshRate) Hz")
}
$out.Add("WPF render tier: $tier " + $(if ($tier -ge 2) { '(drawn by the graphics card)' } elseif ($tier -eq 1) { '(partly by the graphics card)' } else { '(drawn by the processor alone - WPF is not using the graphics card)' }))
$out.Add('')
$out.Add('While scrolling, page by page (each sample is one second):')
if ($segments.Count -eq 0) { $out.Add('  nothing scrolled') }
foreach ($k in $segments.Keys) {
    $s = $segments[$k]
    $fps = $s | ForEach-Object { $_.Fps }
    $worst = $s | ForEach-Object { $_.Worst }
    $out.Add(('  {0,-34} {1,3} s   median {2,5:F1} fps, lowest {3,5:F1} fps   longest stall {4,4} ms (median {5} ms)' -f $k, $s.Count, (Median $fps), ($fps | Measure-Object -Minimum).Minimum, ($worst | Measure-Object -Maximum).Maximum, (Median $worst)))
}
$out.Add('')

# What held the app's own thread for 50ms or more, most often first. A stall above with nothing
# here was spent drawing rather than in the app's own work.
$busy = @($perf | Select-String 'busy (\d+)ms: (.+)$' | ForEach-Object {
    [pscustomobject]@{ Ms = [int]$_.Matches[0].Groups[1].Value; What = $_.Matches[0].Groups[2].Value }
})
$out.Add('The app itself busy for 50 ms or more, by what (count, longest):')
if ($busy.Count -eq 0) { $out.Add('  nothing') }
foreach ($g in ($busy | Group-Object What | Sort-Object Count -Descending)) {
    $out.Add(('  {0,4}x  longest {1,5} ms  {2}' -f $g.Count, ($g.Group | Measure-Object Ms -Maximum).Maximum, $g.Name))
}
$out.Add('')
$rate = if ($refresh) { " ($refresh Hz here)" } else { '' }
$out.Add("How to read it: smooth is close to the screen's own rate$rate, with stalls under about 50 ms.")
$out.Add('The first and last second of each scroll include still frames, so the median is the number to go by.')
$out.Add('')
$out.Add('Everything the app logged:')
foreach ($l in $perf) { $out.Add('  ' + ($l -replace '^\S+ ', '' -replace '\s+INFO\s+\[Perf\]', '')) }

$dir = Split-Path $env:REPORT
if (-not (Test-Path $dir)) { New-Item -ItemType Directory $dir | Out-Null }
[IO.File]::WriteAllLines($env:REPORT, $out)

Write-Host ''
$last = $out.FindIndex({ param($x) $x.StartsWith('How to read it') })
$out.GetRange(0, $last + 1) | ForEach-Object { Write-Host $_ }
Write-Host ''
Write-Host 'The full report is in "Claude outputs\perf-report.txt". Tell Claude it is there.'
