param([Parameter(Mandatory)][string]$ArchivePath, [string]$UpstreamArchivePath = '')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $ArchivePath).Path)
try {
    $entries = @($zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    $required = @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'BepInEx/core/BepInEx.dll',
        'BepInEx/core/BepInEx.Preloader.dll', 'BepInEx/core/0Harmony.dll',
        'BepInEx/plugins/SephiriaBuildOverlay/SephiriaBuildOverlay.Core.dll',
        'BepInEx/plugins/SephiriaBuildOverlay/SephiriaBuildOverlay.Plugin.dll',
        'licenses/UnityDoorstop-source-v4.5.0.zip', 'licenses/UnityDoorstop-LICENSE.txt', 'README.md')
    foreach ($name in $required) { if ($name -notin $entries) { throw "Missing paste-ready entry: $name" } }
    foreach ($name in $entries) {
        if ($name -match '(^|/)\.\.?(/|$)|^/|:|^(BepInEx/config|BepInEx/scripts|Sephiria_Data)/|Assembly-CSharp|UnityEngine.*\.dll|SephPlanner') {
            throw "Forbidden package entry: $name"
        }
    }
    $manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'bepinex.json') -Raw | ConvertFrom-Json
    if (!$UpstreamArchivePath) { $UpstreamArchivePath = Join-Path $PSScriptRoot ('..\.local\BepInEx\' + $manifest.archive.name) }
    if ((Get-FileHash -LiteralPath $UpstreamArchivePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.archive.sha256) { throw 'Original BepInEx archive hash differs.' }
    $upstream = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $UpstreamArchivePath).Path)
    $allowed = @('README.md', 'docs/DEBUGGING.md', 'docs/FAST_TESTING.md', 'docs/VERIFICATION.md',
        'licenses/THIRD_PARTY_NOTICES.md', 'licenses/dependency-manifest.json',
        'BepInEx/plugins/SephiriaBuildOverlay/SephiriaBuildOverlay.Core.dll',
        'BepInEx/plugins/SephiriaBuildOverlay/SephiriaBuildOverlay.Plugin.dll')
    function EntryHash($entry) {
        $stream = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
        try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
        finally { $stream.Dispose(); $sha.Dispose() }
    }
    try {
        foreach ($original in $upstream.Entries) {
            if ($original.FullName.EndsWith('/')) { continue }
            $name = $original.FullName.Replace('\', '/')
            $allowed += $name
            $packaged = $zip.GetEntry($name)
            if (!$packaged -or (EntryHash $packaged) -ne (EntryHash $original)) { throw "Upstream BepInEx file changed/missing: $name" }
        }
    } finally { $upstream.Dispose() }
    foreach ($dependency in @($manifest.licenses) + @($manifest.source)) {
        $entry = $zip.GetEntry('licenses/' + $dependency.name)
        $allowed += 'licenses/' + $dependency.name
        if (!$entry) { throw "Missing dependency notice/source: $($dependency.name)" }
        $stream = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
        try { $digest = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
        finally { $stream.Dispose(); $sha.Dispose() }
        if ($digest -ne $dependency.sha256) { throw "Altered notice/source: $($dependency.name)" }
    }
    foreach ($name in $entries) { if ($name -notin $allowed) { throw "Unexpected package payload: $name" } }
    $stream = $zip.GetEntry('winhttp.dll').Open()
    $memory = New-Object IO.MemoryStream
    try { $stream.CopyTo($memory); $bytes = $memory.ToArray() }
    finally { $stream.Dispose(); $memory.Dispose() }
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
    if ([BitConverter]::ToUInt16($bytes, $peOffset + 4) -ne 0x8664) { throw 'Loader is not Windows x64.' }
    Write-Output "Package checks passed: $($entries.Count) entries; loader, plugin, notices, source, no game/user configuration."
} finally { $zip.Dispose() }
