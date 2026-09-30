param(
    [Parameter(Mandatory)][string]$BuildJsonPath,
    [Parameter(Mandatory)][Guid]$BuildId,
    [Parameter(Mandatory)][ValidateSet('Required', 'Recommended', 'Excluded')][string[]]$Roles,
    [Parameter(Mandatory)][string]$CacheDirectory,
    [switch]$Activate
)
$ErrorActionPreference = 'Stop'
$sourceFile = Get-Item -LiteralPath $BuildJsonPath
if ($sourceFile.Length -gt 2MB) { throw 'Build response exceeds 2 MiB.' }
$raw = Get-Content -LiteralPath $sourceFile.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
$data = if ($raw.data) { $raw.data } else { $raw }
if ([Guid]$data.postUuid -ne $BuildId) { throw 'Build UUID does not match the explicitly approved build.' }
if ($data.content.Count -ne $Roles.Count) { throw 'Every section needs an explicit role. No classification defaults are inferred.' }
$roleValues = @{ Required = 1; Recommended = 2; Excluded = 3 }
$importedSections = @()
$reviewedSections = @()
for ($sectionIndex = 0; $sectionIndex -lt $data.content.Count; $sectionIndex++) {
    $section = $data.content[$sectionIndex]
    $importedItems = @()
    $reviewedItems = @()
    for ($itemIndex = 0; $itemIndex -lt $section.items.Count; $itemIndex++) {
        $item = $section.items[$itemIndex]
        $instanceId = if ($item.id) { [string]$item.id } else { "${sectionIndex}:${itemIndex}" }
        if ([string]::IsNullOrWhiteSpace($item.value)) { throw 'Missing item slug.' }
        $importedItems += @{ InstanceId = $instanceId; Slug = [string]$item.value }
        $reviewedItems += @{ Id = $instanceId; Slug = [string]$item.value; Desired = 1; Role = $null; Priority = $null; Mapping = $null }
    }
    $importedSections += @{ Id = [string]$sectionIndex; Label = [string]$section.label; Description = [string]$section.description; Items = @($importedItems) }
    $reviewedSections += @{ Id = [string]$sectionIndex; Role = $roleValues[$Roles[$sectionIndex]]; Priority = $sectionIndex; Items = @($reviewedItems) }
}
$checkpoint = @{
    Schema = 1; WasActivated = [bool]$Activate; Sections = @($reviewedSections)
    Build = @{
        Id = $BuildId.ToString('D'); Title = [string]$data.title; GameVersion = [string]$data.version
        WeaponSlug = $data.weapon; MiracleSlug = $data.miracle; Sections = @($importedSections)
        Talents = $data.ability; Combos = @($data.combo | Where-Object { $_ }); CostumeSlug = $data.costume
    }
}
New-Item -ItemType Directory -Path $CacheDirectory -Force | Out-Null
$target = Join-Path $CacheDirectory 'review.json'
if (Test-Path -LiteralPath $target) { Copy-Item -LiteralPath $target -Destination ($target + '.before-dev-review') -Force }
$temporary = Join-Path $CacheDirectory ([Guid]::NewGuid().ToString('N') + '.review.tmp')
[IO.File]::WriteAllText($temporary, ($checkpoint | ConvertTo-Json -Depth 30), [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temporary -Destination $target -Force
Write-Output 'Approved review saved. Reload the plugin to revalidate game bindings and restore it. No game action was executed.'
