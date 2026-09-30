param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Sephiria',
    [ValidateSet('snapshot', 'catalog', 'preview', 'controller-preview')][string]$Command = 'snapshot',
    [ValidateRange(1, 60)][int]$TimeoutSeconds = 10
)
$ErrorActionPreference = 'Stop'
$probeDirectory = Join-Path $GameDir 'BepInEx\cache\SephiriaBuildOverlay\diagnostics'
New-Item -ItemType Directory -Path $probeDirectory -Force | Out-Null
$requestId = [Guid]::NewGuid().ToString('D')
$requestPath = Join-Path $probeDirectory 'request.json'
$temporaryPath = Join-Path $probeDirectory "$requestId.request.tmp"
$request = @{ id = $requestId; command = $Command } | ConvertTo-Json
[IO.File]::WriteAllText($temporaryPath, $request, [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temporaryPath -Destination $requestPath -Force
$resultPath = Join-Path $probeDirectory "$requestId.json"
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
while ([DateTime]::UtcNow -lt $deadline) {
    if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8; return }
    Start-Sleep -Milliseconds 100
}
throw 'No runtime response. Check Debug.RuntimeDiagnostics, game process and BepInEx log. This command cannot execute game actions.'
