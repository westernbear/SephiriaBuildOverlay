param(
    [Parameter(Mandatory = $true)][string]$SnapshotPath,
    [string]$CatalogPath = (Join-Path $PSScriptRoot '..\src\SephiriaBuildOverlay.Core\Catalog\catalog-1.0.33.json')
)

$ErrorActionPreference = 'Stop'
$snapshot = Get-Content -Encoding UTF8 -Raw -LiteralPath $SnapshotPath | ConvertFrom-Json
$catalog = Get-Content -Encoding UTF8 -Raw -LiteralPath $CatalogPath | ConvertFrom-Json
if ($snapshot.gameVersion -ne $catalog.gameVersion -or -not $snapshot.entries) {
    throw 'Runtime snapshot must declare the same gameVersion as the catalog.'
}

# Website combo slugs and the game's canonical category IDs describe the same categories.
$categoryAliases = @{
    extrium = 'darkcloud'; magic_engineering = 'magitech'; spring_song = 'windsong'
    firmness = 'sturdy'; yinggalbul = 'ember'; bargaining = 'savvy'; colleague = 'companion'
    element = 'elemental'; ice_weapon = 'frost'; mystery = 'mystic'; sun_sword = 'flamesword'
}
function CanonicalCategory([string]$category) {
    (($category -split ',' | Where-Object { $_ } | ForEach-Object {
        $value = $_.ToLowerInvariant()
        if ($categoryAliases.ContainsKey($value)) { $categoryAliases[$value] } else { $value }
    } | Sort-Object -Unique) -join ',')
}
function CanonicalName([string]$name) { $name -replace '\s+', '' }

$oldToNewWeaponKeys = @{}
$updated = 0
$unresolved = 0
foreach ($entry in $catalog.entries) {
    $oldKey = $entry.gameKey
    if ($entry.PSObject.Properties['category']) { $entry.category = CanonicalCategory $entry.category }
    # Only a unique metadata-backed match becomes a deployed game ID. Unknown IDs must not collide with real IDs.
    $matches = @($snapshot.entries | Where-Object {
        $_.Kind -eq $entry.kind -and (CanonicalName $_.KoreanName) -eq (CanonicalName $entry.koreanName) -and
        ($entry.kind -ne 'Artifact' -or (
            (($_.Rarity -eq $entry.rarity) -or ($entry.rarity -eq 'Eternal' -and $_.Rarity -eq 'Rare' -and $_.IsDual -eq $true)) -and
            (-not $entry.PSObject.Properties['isDual'] -or $_.IsDual -eq $entry.isDual) -and
            (CanonicalCategory $_.Category) -eq $entry.category))
    } | Group-Object GameKey | Where-Object { $_.Count -eq 1 } | ForEach-Object { $_.Group[0] })
    if ($matches.Count -eq 1) {
        $entry.gameKey = [string]$matches[0].GameKey
        $entry.koreanName = [string]$matches[0].KoreanName
        if ($entry.kind -eq 'Artifact' -and $matches[0].PSObject.Properties['IsDual']) {
            $entry.rarity = [string]$matches[0].Rarity
            $entry | Add-Member -NotePropertyName isDual -NotePropertyValue ([bool]$matches[0].IsDual) -Force
        }
        $updated++
    } else {
        $entry.gameKey = 'unresolved:' + $entry.kind.ToLowerInvariant() + ':' + $entry.slug
        $unresolved++
    }
    if ($entry.kind -eq 'Weapon') { $oldToNewWeaponKeys[$oldKey] = $entry.gameKey }
}
foreach ($entry in $catalog.entries | Where-Object kind -eq 'Weapon') {
    if ($entry.parentGameKey) {
        if (-not $oldToNewWeaponKeys.ContainsKey($entry.parentGameKey)) { throw "Missing parent for $($entry.slug)" }
        $entry.parentGameKey = $oldToNewWeaponKeys[$entry.parentGameKey]
    }
}
# Preserve the website's tier/parent expectations: runtime verification must still validate the upgrade graph.
$catalog | Add-Member -NotePropertyName runtimeVerifiedVersion -NotePropertyValue $snapshot.gameVersion -Force
$json = $catalog | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText((Resolve-Path -LiteralPath $CatalogPath).Path, $json, [System.Text.UTF8Encoding]::new($false))
Write-Output "Matched IDs: $updated; unresolved (automatic actions disabled): $unresolved"
