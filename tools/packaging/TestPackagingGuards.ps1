param([Parameter(Mandatory)][string]$ArchivePath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$testRoot = Join-Path $PSScriptRoot ('..\.local\packaging-test-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$cache = Join-Path $testRoot 'cache'
[IO.Directory]::CreateDirectory($cache) | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\..\README.md') -Destination (Join-Path $cache 'BepInEx_win_x64_5.4.23.5.zip')
try {
    & (Join-Path $PSScriptRoot 'BundleBepInEx.ps1') -CacheDirectory $cache -PackageDirectory (Join-Path $testRoot 'output')
    throw 'Guard failed: corrupt upstream archive was accepted.'
} catch {
    if ($_.Exception.Message -notmatch '^Dependency SHA-256 mismatch:') { throw }
}
$brokenPackage = Join-Path $testRoot 'missing-loader.zip'
Copy-Item -LiteralPath $ArchivePath -Destination $brokenPackage
$zip = [IO.Compression.ZipFile]::Open($brokenPackage, [IO.Compression.ZipArchiveMode]::Update)
try { $zip.GetEntry('winhttp.dll').Delete() } finally { $zip.Dispose() }
try {
    & (Join-Path $PSScriptRoot 'TestReleasePackage.ps1') -ArchivePath $brokenPackage
    throw 'Guard failed: package missing loader was accepted.'
} catch {
    if ($_.Exception.Message -ne 'Missing paste-ready entry: winhttp.dll') { throw }
}
Write-Output 'PASS: corrupt dependency and incomplete loader package were rejected. Original artifacts were preserved.'
foreach ($entryName in @('BepInEx/patchers/SephiriaBuildOverlay.Updater.dll', 'update-protocol.txt')) {
    $incomplete = Join-Path $testRoot ([Guid]::NewGuid().ToString('N') + '.zip')
    Copy-Item -LiteralPath $ArchivePath -Destination $incomplete
    $zip = [IO.Compression.ZipFile]::Open($incomplete, [IO.Compression.ZipArchiveMode]::Update)
    try { $zip.GetEntry($entryName).Delete() } finally { $zip.Dispose() }
    try {
        & (Join-Path $PSScriptRoot 'TestReleasePackage.ps1') -ArchivePath $incomplete
        throw 'Guard failed: package without updater bootstrap/protocol was accepted.'
    } catch {
        if ($_.Exception.Message -ne "Missing paste-ready entry: $entryName") { throw }
    }
}
Write-Output 'PASS: missing auto-update preloader/protocol packages were rejected.'
