param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Sephiria'
)
$ErrorActionPreference = 'Stop'
$gamePath = (Resolve-Path -LiteralPath $GameDir).Path
if (!(Test-Path -LiteralPath (Join-Path $gamePath 'Sephiria.exe'))) { throw 'Target is not a Sephiria game directory.' }
if (Get-Process -Name Sephiria -ErrorAction SilentlyContinue) { throw 'Close Sephiria before changing DLL placement.' }
$packagePath = (Resolve-Path -LiteralPath $PackageDirectory).Path
$packagePlugins = Join-Path $packagePath 'BepInEx\plugins\SephiriaBuildOverlay'
$names = @('SephiriaBuildOverlay.Core.dll', 'SephiriaBuildOverlay.Plugin.dll')
foreach ($name in $names) {
    if (!(Test-Path -LiteralPath (Join-Path $packagePlugins $name) -PathType Leaf)) { throw "Package lacks $name" }
}
$localRoot = Join-Path $PSScriptRoot '..\.local'
$backupPath = Join-Path $localRoot ('production-restore-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $backupPath -Force | Out-Null
$scriptsPath = Join-Path $gamePath 'BepInEx\scripts'
$pluginsPath = Join-Path $gamePath 'BepInEx\plugins\SephiriaBuildOverlay'
$targets = @(
    (Join-Path $scriptsPath 'SephiriaBuildOverlay.Plugin.dll'),
    (Join-Path $scriptsPath 'SephiriaBuildOverlay.Plugin.pdb')
)
$otherScripts = @(Get-ChildItem -LiteralPath $scriptsPath -Filter '*.dll' -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne 'SephiriaBuildOverlay.Plugin.dll' })
if ($otherScripts.Count -eq 0) { $targets += Join-Path $gamePath 'BepInEx\plugins\ScriptEngine.dll' }
foreach ($target in $targets) {
    if (Test-Path -LiteralPath $target -PathType Leaf) { Move-Item -LiteralPath $target -Destination $backupPath }
}
New-Item -ItemType Directory -Path $pluginsPath -Force | Out-Null
foreach ($name in $names) {
    $destination = Join-Path $pluginsPath $name
    if (Test-Path -LiteralPath $destination) { Copy-Item -LiteralPath $destination -Destination (Join-Path $backupPath ($name + '.previous')) }
    Copy-Item -LiteralPath (Join-Path $packagePlugins $name) -Destination $destination -Force
}
Write-Output "Production DLLs installed. Recoverable backup: $backupPath"
Write-Output 'Set Debug.RuntimeDiagnostics / MeasurePerformance to false for normal play; existing config and other mods were preserved.'
