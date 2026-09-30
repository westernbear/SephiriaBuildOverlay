param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Sephiria',
    [Guid]$ExpectedBuild = [Guid]::Empty,
    [ValidateRange(2, 20)][int]$Samples = 3
)
$ErrorActionPreference = 'Stop'
$baseline = $null
for ($sample = 0; $sample -lt $Samples; $sample++) {
    $response = (& (Join-Path $PSScriptRoot 'InvokeRuntimeProbe.ps1') -GameDir $GameDir -Command snapshot | ConvertFrom-Json).result
    $snapshot = $response.snapshot
    if (!$snapshot.IsLocalPlayerOwned -or $snapshot.ServerRequestPending) { throw 'Requires an idle, locally owned player.' }
    if ($ExpectedBuild -ne [Guid]::Empty -and $response.overlay.activeBuild -ne $ExpectedBuild.ToString('D')) { throw 'Expected reviewed build was not restored.' }
    $inventorySignature = @($snapshot.Inventory | Sort-Object InstanceId | ForEach-Object { "$($_.InstanceId):$($_.CatalogKey):$($_.X):$($_.Y)" }) -join '|'
    $signature = "$($snapshot.RunId):$($snapshot.LocalPlayerId):$($snapshot.Money):$($snapshot.SharedDice):$inventorySignature"
    if ($null -ne $baseline -and $signature -ne $baseline) { throw 'Game state changed during this read-only smoke check; rerun in an idle scene.' }
    $baseline = $signature
    if ($response.overlay.overlayVisible -and !$response.overlay.importVisible) {
        $native = $response.board.overlay
        if (!$native.visible -or !$native.passive -or $native.font -ne $response.board.font.asset) { throw 'Native passive overlay / game font check failed.' }
        if (@($native.labels | Where-Object { $_.key -like 'caption:*' }).Count -ne 0) { throw 'Obsolete always-visible item captions remain.' }
        $action = $response.overlay.recommendation.Action
        if ($null -ne $action -and ($action.MoneyCost -gt 0 -or $action.DiceCost -gt 0)) {
            $warning = @($native.labels | Where-Object { $_.key -eq "warning:$($action.TargetToken)" })
            if ($warning.Count -ne 1 -or [string]::IsNullOrWhiteSpace($warning[0].text)) { throw 'The recommended consumption has no visible cost warning.' }
        }
    }
}
Write-Output "PASS: $Samples stable, locally owned snapshots; no game action sent."
Write-Output "Screen=$($snapshot.Screen), candidates=$(@($snapshot.Candidates).Count), font=$($response.board.font.asset), nativeElements=$($response.board.overlay.elements)"
