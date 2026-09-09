# Builds the plugin and zips a Thunderstore-ready package into dist/.
#
#   powershell -ExecutionPolicy Bypass -File tools\pack.ps1
#
# Thunderstore wants manifest.json, icon.png and README.md at the zip root, so the
# package/ folder is zipped by its contents, not as a folder.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$pkg = Join-Path $root "package"
$dist = Join-Path $root "dist"

$manifest = Get-Content (Join-Path $pkg "manifest.json") -Raw | ConvertFrom-Json
$version = $manifest.version_number

# The plugin version and the package version are read by different people in different
# places; if they drift, the Thunderstore listing lies about what is in the DLL.
# This mod carries its version in the [BepInPlugin] attribute rather than a constant.
$plugin = Get-Content (Join-Path $root "src\Plugin.cs") -Raw
if ($plugin -notmatch 'BepInPlugin\([^)]*,\s*"([0-9.]+)"\s*\)') { throw "Could not read the plugin version from src\Plugin.cs" }
if ($Matches[1] -ne $version) { throw "manifest.json is $version but the plugin says $($Matches[1])" }

dotnet build (Join-Path $root "src\CarturHDBlood.csproj") -c Release
if (-not $?) { throw "build failed" }

# Cleared, not just overwritten: a DLL left behind from an earlier build would otherwise
# ride along in the zip and get loaded next to the current one.
$plugins = Join-Path $pkg "plugins"
if (Test-Path $plugins) { Remove-Item $plugins -Recurse -Force }
New-Item -ItemType Directory -Force -Path $plugins | Out-Null
Copy-Item (Join-Path $root "src\bin\Release\net472\CarturHDBlood.dll") $plugins -Force

New-Item -ItemType Directory -Force -Path $dist | Out-Null
$zip = Join-Path $dist "Carturs_HD_Blood-$version.zip"
if (Test-Path $zip) { Remove-Item $zip }

# Written entry by entry rather than with Compress-Archive: that cmdlet stores Windows
# backslashes in the entry names, and an installer reading the zip then creates a single
# file literally named "plugins\CarturHDBlood.dll" instead of the folder.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, "Create")
try {
    foreach ($file in Get-ChildItem $pkg -Recurse -File) {
        $name = $file.FullName.Substring($pkg.Length + 1).Replace("\", "/")
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $name) | Out-Null
    }
}
finally { $archive.Dispose() }

Write-Host "packed $zip"
