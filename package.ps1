param([string]$Version = '1.1.1')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$example = Get-Content -LiteralPath (Join-Path $root 'settings.example.json') -Raw | ConvertFrom-Json
if ($example.Executable -or $example.Model -or $example.Mmproj -or $example.LanAddress -or $example.LanEnabled) {
    throw 'settings.example.json must not contain local model paths, projector paths, or LAN settings.'
}

Push-Location $root
try { & (Join-Path $root 'build.ps1') } finally { Pop-Location }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$filename = "KVMem-Desktop-v$Version.zip"
$archivePath = Join-Path $dist $filename
if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath -Force }
$files = @{
    'KVMem.exe' = 'KVMem.exe'
    'KVMem.ico' = 'KVMem.ico'
    'KVMem.png' = 'KVMem.png'
    'control.ps1' = 'control.ps1'
    'ollama-kvmem-bridge.cjs' = 'ollama-kvmem-bridge.cjs'
    'LICENSE' = 'LICENSE'
    'README.md' = 'README.md'
    'README.zh-CN.md' = 'README.zh-CN.md'
    'settings.json' = 'settings.example.json'
}
$zip = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($entry in $files.GetEnumerator()) {
        $source = Join-Path $root $entry.Value
        if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Package file missing: $source" }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $source, $entry.Key, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $zip.Dispose() }
$checksum = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash
Set-Content -LiteralPath (Join-Path $dist "KVMem-Desktop-v$Version-SHA256.txt") -Value "$checksum  $filename" -Encoding Ascii
Write-Output "Created $archivePath"
