param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Sephiria',
    [ValidateRange(5,300)][int]$DurationSeconds = 45,
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
if (!$OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot ('..\.local\multiplayer-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')) }
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$logPath = Join-Path $GameDir 'BepInEx\LogOutput.log'
$beforeLog = if (Test-Path -LiteralPath $logPath) { @(Get-Content -LiteralPath $logPath -Encoding UTF8).Count } else { 0 }
$observations = [Collections.Generic.List[object]]::new()
$deadline = [DateTime]::UtcNow.AddSeconds($DurationSeconds)
$index = 0
Write-Output 'Read-only capture started. Follow the scenario instructions and operate the game yourself. No keys, confirmations or game actions are injected.'
while ([DateTime]::UtcNow -lt $deadline) {
    $raw = & (Join-Path $PSScriptRoot 'InvokeRuntimeProbe.ps1') -GameDir $GameDir -Command snapshot
    $probe = $raw | ConvertFrom-Json
    [IO.File]::WriteAllText((Join-Path $outputPath ('snapshot-{0:d3}.json' -f $index)), $raw, [Text.UTF8Encoding]::new($false))
    $observations.Add($probe)
    $index++
    Start-Sleep -Milliseconds 500
}
if (Test-Path -LiteralPath $logPath) {
    Get-Content -LiteralPath $logPath -Encoding UTF8 | Select-Object -Skip $beforeLog |
        Set-Content -LiteralPath (Join-Path $outputPath 'observed-BepInEx.log') -Encoding UTF8
}
$requests = @($observations | ForEach-Object { $_.result.multiplayer.requests } | Where-Object { $_ } |
    Sort-Object -Property utc,requestId,status -Unique)
$summary = @{ snapshots = $index; requests = $requests; gameActionsInjected = $false; remoteClientVerified = $false;
    first = $observations[0].result.snapshot; last = $observations[$observations.Count-1].result.snapshot;
    installedCoreSha256 = (Get-FileHash -LiteralPath (Join-Path $GameDir 'BepInEx\plugins\SephiriaBuildOverlay\SephiriaBuildOverlay.Core.dll')).Hash;
    installedPluginSha256 = (Get-FileHash -LiteralPath (Join-Path $GameDir 'BepInEx\plugins\SephiriaBuildOverlay\SephiriaBuildOverlay.Plugin.dll')).Hash }
$summary | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $outputPath 'summary.json') -Encoding UTF8
Write-Output "Read-only evidence saved: $outputPath"
