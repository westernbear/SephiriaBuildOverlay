param([Parameter(Mandatory)][string]$PackageDirectory, [string]$CacheDirectory = '')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'bepinex.json') -Raw | ConvertFrom-Json
if (!$CacheDirectory) { $CacheDirectory = Join-Path $PSScriptRoot '..\.local\BepInEx' }
[IO.Directory]::CreateDirectory($CacheDirectory) | Out-Null
[IO.Directory]::CreateDirectory($PackageDirectory) | Out-Null
function Get-VerifiedDependency($dependency) {
    $path = Join-Path $CacheDirectory $dependency.name
    if (!(Test-Path -LiteralPath $path)) {
        Invoke-WebRequest -Uri $dependency.url -OutFile $path -UseBasicParsing -TimeoutSec 120
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $dependency.sha256) {
        throw "Dependency SHA-256 mismatch: $($dependency.name). Cache was not modified; remove only this invalid file and retry."
    }
    return $path
}
$archivePath = Get-VerifiedDependency $manifest.archive
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    foreach ($entry in $archive.Entries) {
        $name = $entry.FullName.Replace('\', '/')
        if ($name.EndsWith('/')) { continue }
        if ($name -notmatch '^(BepInEx/core/[A-Za-z0-9._-]+\.(dll|xml)|\.doorstop_version|doorstop_config\.ini|winhttp\.dll|changelog\.txt)$') {
            throw "Unexpected upstream ZIP entry: $name"
        }
        $destination = Join-Path $PackageDirectory $name
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $true)
    }
} finally { $archive.Dispose() }
$licensesDirectory = Join-Path $PackageDirectory 'licenses'
[IO.Directory]::CreateDirectory($licensesDirectory) | Out-Null
foreach ($dependency in @($manifest.licenses) + @($manifest.source)) {
    $verified = Get-VerifiedDependency $dependency
    Copy-Item -LiteralPath $verified -Destination (Join-Path $licensesDirectory $dependency.name) -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bepinex.json') -Destination (Join-Path $licensesDirectory 'dependency-manifest.json')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD_PARTY_NOTICES.md') -Destination $licensesDirectory
Write-Output "Bundled verified BepInEx $($manifest.version) Windows x64, notices and Doorstop corresponding source."
