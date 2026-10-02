param(
    [string]$Configuration = 'Release',
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solution = Join-Path $repositoryRoot 'SephiriaBuildOverlay.sln'
$distDirectory = Join-Path $repositoryRoot 'dist'

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$project = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\SephiriaBuildOverlay.Plugin\SephiriaBuildOverlay.Plugin.csproj')
    $Version = [string]$project.Project.PropertyGroup.Version
}

$Version = $Version.TrimStart('v')
if ($Version -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') {
    throw "Invalid release version: $Version"
}
$pluginSource = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\SephiriaBuildOverlay.Plugin\SephiriaBuildOverlayPlugin.cs') -Raw -Encoding UTF8
if ($pluginSource -notmatch 'const string PluginVersion = "([^\"]+)"' -or $Matches[1] -ne $Version) {
    throw 'Package/tag version must match the BepInEx PluginVersion. Bump source metadata before publishing.'
}

$packageName = "SephiriaBuildOverlay-$Version"
$packageDirectory = Join-Path $distDirectory $packageName
$pluginDirectory = Join-Path $packageDirectory 'BepInEx\plugins\SephiriaBuildOverlay'
$archivePath = Join-Path $distDirectory "$packageName.zip"
$checksumPath = Join-Path $distDirectory "$packageName.sha256"

$distFullPath = [System.IO.Path]::GetFullPath($distDirectory)
$packageFullPath = [System.IO.Path]::GetFullPath($packageDirectory)
if (-not $packageFullPath.StartsWith($distFullPath + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean a package path outside dist: $packageFullPath"
}

if (Test-Path -LiteralPath $packageFullPath) {
    Remove-Item -LiteralPath $packageFullPath -Recurse -Force
}
if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
if (Test-Path -LiteralPath $checksumPath) {
    Remove-Item -LiteralPath $checksumPath -Force
}

dotnet build $solution -c $Configuration -p:Version="$Version"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

[System.IO.Directory]::CreateDirectory($pluginDirectory) | Out-Null
$core = Join-Path $repositoryRoot "src\SephiriaBuildOverlay.Core\bin\$Configuration\netstandard2.1\SephiriaBuildOverlay.Core.dll"
$plugin = Join-Path $repositoryRoot "src\SephiriaBuildOverlay.Plugin\bin\$Configuration\netstandard2.1\SephiriaBuildOverlay.Plugin.dll"
Copy-Item -LiteralPath $core -Destination $pluginDirectory -Force
Copy-Item -LiteralPath $plugin -Destination $pluginDirectory -Force
$patcherDirectory = Join-Path $packageDirectory 'BepInEx\patchers'
[System.IO.Directory]::CreateDirectory($patcherDirectory) | Out-Null
Copy-Item -LiteralPath (Join-Path $repositoryRoot "src\SephiriaBuildOverlay.Updater\bin\$Configuration\netstandard2.1\SephiriaBuildOverlay.Updater.dll") -Destination $patcherDirectory -Force
[IO.File]::WriteAllText((Join-Path $packageDirectory 'update-protocol.txt'), "1`n$Version`n", (New-Object Text.UTF8Encoding($false)))
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') -Destination $packageDirectory -Force
$packageDocsDirectory = Join-Path $packageDirectory 'docs'
[System.IO.Directory]::CreateDirectory($packageDocsDirectory) | Out-Null
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\DEBUGGING.md') -Destination $packageDocsDirectory -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\FAST_TESTING.md') -Destination $packageDocsDirectory -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\VERIFICATION.md') -Destination $packageDocsDirectory -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\AUTO_UPDATE.md') -Destination $packageDocsDirectory -Force

& (Join-Path $PSScriptRoot 'packaging\BundleBepInEx.ps1') -PackageDirectory $packageDirectory
# Compress-Archive silently omits hidden entries such as .doorstop_version.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($archivePath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $packageDirectory -Recurse -File -Force) {
        $entryName = $file.FullName.Substring($packageFullPath.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $zip.Dispose() }
& (Join-Path $PSScriptRoot 'packaging\TestReleasePackage.ps1') -ArchivePath $archivePath
& (Join-Path $PSScriptRoot 'packaging\TestPackagingGuards.ps1') -ArchivePath $archivePath
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Value "$hash  $([System.IO.Path]::GetFileName($archivePath))" -Encoding ascii

Write-Output "Paste-ready directory: $packageDirectory"
Write-Output "Release archive: $archivePath"
Write-Output "SHA-256: $hash"
