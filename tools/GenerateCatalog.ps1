param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\SephiriaBuildOverlay.Core\Catalog\catalog-1.0.33.json')
)

$ErrorActionPreference = 'Stop'
$baseUrl = 'https://www.sephiria.wiki'

function Get-Page([string]$path) {
    $client = [System.Net.WebClient]::new()
    try {
        [System.Text.Encoding]::UTF8.GetString($client.DownloadData("$baseUrl/$path"))
    }
    finally {
        $client.Dispose()
    }
}

function Decode([string]$value) {
    [regex]::Unescape($value)
}

$entries = [System.Collections.Generic.List[object]]::new()
$artifactHtml = Get-Page 'artifact'
$artifactPattern = '\\"id\\":(?<id>\d+),\\"value\\":\\"(?<slug>[^\\"]+)\\",\\"label_kor\\":\\"(?<name>[^\\"]+)\\",\\"label_eng\\":\\"[^\\"]*\\",\\"tier\\":\\"(?<tier>[^\\"]+)\\",\\"effect\\":\{\\"sets\\":\[(?<sets>.*?)\]'
$rarities = @{ common = 'Common'; advanced = 'Uncommon'; rare = 'Rare'; legend = 'Legend'; solid = 'Eternal' }
foreach ($match in [regex]::Matches($artifactHtml, $artifactPattern)) {
    $setNames = [regex]::Matches($match.Groups['sets'].Value, '\\"(?<set>[^\\"]+)\\"') |
        ForEach-Object { $_.Groups['set'].Value } | Sort-Object -Unique
    $entries.Add([ordered]@{
        slug = $match.Groups['slug'].Value
        gameKey = $match.Groups['id'].Value
        kind = 'Artifact'
        koreanName = (Decode $match.Groups['name'].Value)
        rarity = $rarities[$match.Groups['tier'].Value]
        category = ($setNames -join ',')
    })
}

$weaponHtml = Get-Page 'weapon'
$weaponPattern = '\\"id\\":(?<id>\d+),\\"value\\":\\"(?<slug>[^\\"]+)\\",\\"value_kor\\":\\"(?<name>[^\\"]+)\\",\\"tier\\":(?<tier>\d+),\\"parent\\":(?:(?:\\"(?<parent>[^\\"]+)\\")|null)'
$weapons = [regex]::Matches($weaponHtml, $weaponPattern) | ForEach-Object {
    [pscustomobject]@{
        Id = $_.Groups['id'].Value
        Slug = $_.Groups['slug'].Value
        Name = (Decode $_.Groups['name'].Value)
        Tier = [int]$_.Groups['tier'].Value
        Parent = $_.Groups['parent'].Value
    }
}
$weaponKeyBySlug = @{}
foreach ($weapon in $weapons) { $weaponKeyBySlug[$weapon.Slug] = $weapon.Id }
foreach ($weapon in $weapons) {
    $entry = [ordered]@{
        slug = $weapon.Slug
        gameKey = $weapon.Id
        kind = 'Weapon'
        koreanName = $weapon.Name
        tier = $weapon.Tier
    }
    if ($weapon.Parent) { $entry.parentGameKey = $weaponKeyBySlug[$weapon.Parent] }
    $entries.Add($entry)
}

$miracleHtml = Get-Page 'miracle'
$miraclePattern = '\\"value\\":\\"(?<slug>[^\\"]+)\\",\\"value_kor\\":\\"(?<name>[^\\"]+)\\",\\"image\\":\\"[^\\"]*/miracle/'
foreach ($match in [regex]::Matches($miracleHtml, $miraclePattern)) {
    $entries.Add([ordered]@{
        slug = $match.Groups['slug'].Value
        gameKey = $match.Groups['slug'].Value
        kind = 'Miracle'
        koreanName = (Decode $match.Groups['name'].Value)
    })
}

$uniqueEntries = $entries | Group-Object { "$($_.kind):$($_.slug)" } | ForEach-Object { $_.Group[0] }
if (($uniqueEntries | Where-Object kind -eq 'Artifact').Count -lt 250) { throw 'Artifact extraction returned too few entries.' }
if (($uniqueEntries | Where-Object kind -eq 'Weapon').Count -lt 50) { throw 'Weapon extraction returned too few entries.' }
if (($uniqueEntries | Where-Object kind -eq 'Miracle').Count -lt 10) { throw 'Miracle extraction returned too few entries.' }

$document = [ordered]@{
    gameVersion = '1.0.33'
    generatedFrom = $baseUrl
    entries = @($uniqueEntries)
}
$json = $document | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText((Resolve-Path (Split-Path $OutputPath)).Path + '\' + (Split-Path $OutputPath -Leaf), $json, [System.Text.UTF8Encoding]::new($false))
Write-Output "Generated $($uniqueEntries.Count) entries at $OutputPath"
