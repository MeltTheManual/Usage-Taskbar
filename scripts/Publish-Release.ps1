# Builds the file that goes on a GitHub Release: one self-contained Usage.exe that needs nothing installed.
#
# This is deliberately different from Publish-Usage.ps1. That one is the fast local build and is
# framework-dependent, so it needs the .NET Desktop Runtime already on the machine, which is fine for us and
# useless for a stranger downloading a single file. This one carries .NET inside the exe.
#
# Compression is on. It roughly halves the download at the cost of about a second on the very first launch,
# because the bundle unpacks to a cache folder once. That is the right trade for something people download.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'out\release'

# Stale files here matter, because the self-install copies everything sitting beside the exe. This script
# deliberately does not clear the folder for you. Deleting files that someone else put somewhere is not a
# build step's job, so it stops and lets you look instead.
if (Test-Path $out) {
    $stale = Get-ChildItem $out -File | Where-Object { $_.Name -ne 'Usage.exe' }
    if ($stale) {
        Write-Warning "Files other than Usage.exe are already in $out :"
        $stale | ForEach-Object { Write-Warning ("  " + $_.Name) }
        throw "Clear or move those files yourself, then re-run. Refusing to delete them."
    }
}

dotnet publish (Join-Path $root 'src\Usage.App\Usage.App.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $out

if ($LASTEXITCODE -ne 0) {
    throw "Release publish failed."
}

$exe = Get-Item (Join-Path $out 'Usage.exe')
$hash = (Get-FileHash $exe.FullName -Algorithm SHA256).Hash

# Anything other than the single exe means the build is not actually self-contained, and shipping just the
# exe would produce a download that fails on someone else's machine.
$extra = Get-ChildItem $out -File | Where-Object { $_.Name -ne 'Usage.exe' }
if ($extra) {
    Write-Warning "Unexpected extra files in the release output:"
    $extra | ForEach-Object { Write-Warning ("  " + $_.Name) }
    throw "The release build must be a single Usage.exe. Do not upload this."
}

Write-Host ""
Write-Host ("Release build ready: {0}" -f $exe.FullName)
Write-Host ("Size:   {0:N1} MB" -f ($exe.Length / 1MB))
Write-Host ("SHA256: {0}" -f $hash)
Write-Host ""
Write-Host "Publish this SHA256 alongside the download so people can check what they got."
