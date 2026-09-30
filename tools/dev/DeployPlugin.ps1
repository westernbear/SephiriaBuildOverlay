param([string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Sephiria')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$scriptEngine = Join-Path $GameDir 'BepInEx\plugins\ScriptEngine.dll'
if (!(Test-Path -LiteralPath $scriptEngine)) { throw 'ScriptEngine must be installed for dev reload. See docs/FAST_TESTING.md.' }
if (Test-Path -LiteralPath (Join-Path $GameDir 'BepInEx\plugins\SephiriaBuildOverlay\SephiriaBuildOverlay.Plugin.dll')) {
    throw 'Duplicate production plugin detected. Switch to dev mode with the game closed first.'
}
dotnet build (Join-Path $projectRoot 'src\SephiriaBuildOverlay.Plugin\SephiriaBuildOverlay.Plugin.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed. Installed DLL was not changed.' }
$buildOutput = Join-Path $projectRoot 'src\SephiriaBuildOverlay.Plugin\bin\Release\netstandard2.1'
$scriptsDirectory = Join-Path $GameDir 'BepInEx\scripts'
New-Item -ItemType Directory -Path $scriptsDirectory -Force | Out-Null
# Dependencies cannot safely be hot-reloaded. Fail if a Core edit would produce
# a misleading successful deployment against the old in-process Core assembly.
$installedCore = Join-Path $GameDir 'BepInEx\plugins\SephiriaBuildOverlay\SephiriaBuildOverlay.Core.dll'
if (!(Test-Path -LiteralPath $installedCore) -or
    (Get-FileHash -LiteralPath $installedCore).Hash -ne (Get-FileHash -LiteralPath (Join-Path $buildOutput 'SephiriaBuildOverlay.Core.dll')).Hash) {
    throw 'Core changed: stop the game, deploy Core, then restart. Plugin-only hot reload was not performed.'
}
# ScriptEngine reads portable symbols; write PDB before the DLL watcher fires.
Copy-Item -LiteralPath (Join-Path $buildOutput 'SephiriaBuildOverlay.Plugin.pdb') -Destination $scriptsDirectory
Copy-Item -LiteralPath (Join-Path $buildOutput 'SephiriaBuildOverlay.Plugin.dll') -Destination $scriptsDirectory
# Copy-Item preserves timestamps. Explicitly touch the destination so a request
# to reload the same binary (e.g. a new approved review) is not silently ignored.
[IO.File]::SetLastWriteTimeUtc((Join-Path $scriptsDirectory 'SephiriaBuildOverlay.Plugin.dll'), [DateTime]::UtcNow)
Write-Output 'Plugin deployed; ScriptEngine should reload after 2 seconds. Confirm PID and reload result in BepInEx/LogOutput.log.'
