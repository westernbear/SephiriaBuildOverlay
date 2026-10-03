param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Sephiria',
    [string]$SteamExe = 'C:\Program Files (x86)\Steam\steam.exe',
    [string]$ExpectedVersion = '0.1.18',
    [switch]$NativeUiContract,
    [switch]$StartingCatalogContract,
    [ValidateRange(30, 180)][int]$TimeoutSeconds = 120
)
$ErrorActionPreference = 'Stop'
if (Get-Process -Name Sephiria -ErrorAction SilentlyContinue) { throw 'Close the game normally before starting the title-only exit test.' }
if (!(Test-Path -LiteralPath (Join-Path $GameDir 'Sephiria.exe'))) { throw 'Invalid game directory.' }
$started = [DateTime]::UtcNow
$dumpDirectory = Join-Path $env:LOCALAPPDATA 'CrashDumps'
$before = @(Get-ChildItem -LiteralPath $dumpDirectory -Filter 'Sephiria*.dmp' -File -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name)
$launchArgs = @('-applaunch', '2436940', '--sbo-exit-smoke')
if ($NativeUiContract) { $launchArgs += '--sbo-native-ui-smoke' }
if ($StartingCatalogContract) { $launchArgs += '--sbo-starting-catalog-smoke' }
Start-Process -FilePath $SteamExe -ArgumentList $launchArgs -WindowStyle Hidden
$deadline = $started.AddSeconds($TimeoutSeconds)
$gameProcess = $null
while ([DateTime]::UtcNow -lt $deadline) {
    $gameProcess = Get-Process -Name Sephiria -ErrorAction SilentlyContinue | Where-Object { $_.StartTime.ToUniversalTime() -ge $started.AddSeconds(-2) } | Select-Object -First 1
    if ($gameProcess) { break }
    Start-Sleep -Milliseconds 250
}
if (!$gameProcess) { throw 'Game process did not launch.' }
$null = $gameProcess.Handle # Keep the exit-code handle alive.
while (!$gameProcess.WaitForExit(250)) {
    if ([DateTime]::UtcNow -ge $deadline) { throw 'Safe title exit timed out. Game left running; no forced termination.' }
}
Start-Sleep -Seconds 3
$newDumps = @(Get-ChildItem -LiteralPath $dumpDirectory -Filter 'Sephiria*.dmp' -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -notin $before -or $_.LastWriteTimeUtc -ge $started })
$log = Get-Content -LiteralPath (Join-Path $GameDir 'BepInEx\LogOutput.log') -Raw -Encoding UTF8
if ($log -notmatch [Regex]::Escape("Sephiria Build Overlay $ExpectedVersion loaded.")) { throw 'Expected plugin version was not loaded.' }
$playerLogPath = Join-Path $env:USERPROFILE 'AppData\LocalLow\TEAMHORAY\Sephiria\Player.log'
$playerLog = Get-Content -LiteralPath $playerLogPath -Raw -Encoding UTF8
# BepInEx's asynchronous file writer may omit the last shutdown lines; Unity's
# native logger still flushes them. Both files must belong to this launch.
if ($log -notmatch "PID=$($gameProcess.Id)\b" -or (Get-Item -LiteralPath $playerLogPath).LastWriteTimeUtc -lt $started) { throw 'Logs do not belong to this process/launch.' }
$combinedLog = $log + "`n" + $playerLog
if ($NativeUiContract -and ($combinedLog -notmatch 'Native UI construction PASS:' -or $combinedLog -match 'Native UI construction FAILED:')) { throw 'Inactive native UI construction did not pass. Logs were preserved.' }
if ($NativeUiContract -and [version]$ExpectedVersion -ge [version]'0.1.17' -and $combinedLog -notmatch 'Modal cursor contract PASS: settingsSurface=System') { throw 'Settings cursor did not use the system surface.' }
if ([version]$ExpectedVersion -ge [version]'0.1.13' -and $combinedLog -notmatch 'Exclusive modal input gates installed: 11 menu callbacks') { throw 'Native menu callback patches were not installed.' }
if ($StartingCatalogContract -and ($combinedLog -notmatch 'Starting catalog PASS:' -or $combinedLog -match 'Starting catalog unavailable at title:')) { throw 'Native starting catalog validation did not pass. Logs were preserved.' }
if ($StartingCatalogContract -and [version]$ExpectedVersion -ge [version]'0.1.18' -and $combinedLog -notmatch 'Starting fruit catalog PASS: exact native IDs=') { throw 'Native fruit ID binding did not pass.' }
if ($combinedLog -notmatch 'Exit smoke: native title QuitGame') { throw 'Normal native title exit path was not observed.' }
if ($combinedLog -notmatch 'Overlay shutdown complete; pending work cancelled. Quit=True') { throw 'Managed shutdown did not finish.' }
if ($gameProcess.ExitCode -ne 0 -or $newDumps.Count -gt 0 -or $playerLog -match 'Crash!!!') {
    throw "Exit failed: code=$($gameProcess.ExitCode), new dumps=$($newDumps.Count). Dumps and game logs were preserved."
}
[PSCustomObject]@{ passed = $true; pid = $gameProcess.Id; exitCode = $gameProcess.ExitCode; version = $ExpectedVersion; newDumps = $newDumps.Count; earlyUiaCleanup = $combinedLog -match 'Early UIA provider cleanup completed'; computerUse = $false; scope = 'title-only normal native quit, not an active-run/attached-UIA reproduction' } | ConvertTo-Json
