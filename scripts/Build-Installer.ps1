# Builds the Windows installer that goes on a GitHub Release.
#
# Two steps: publish the self-contained single-file exe, then wrap it with Inno Setup. Always in that order,
# so the installer can never ship a stale payload.
#
# Needs Inno Setup 6: winget install --id JRSoftware.InnoSetup -e

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$iss = Join-Path $root 'installer\Usage.iss'
$outDir = Join-Path $root 'out\installer'

# winget installs Inno Setup per-user by default, so the Program Files paths are often the wrong guess.
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw "Inno Setup 6 was not found. Install it with: winget install --id JRSoftware.InnoSetup -e"
}

Write-Host "Publishing the payload first..." -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'Publish-Release.ps1')
if ($LASTEXITCODE -ne 0) {
    throw "The release publish failed, so the installer was not built."
}

Write-Host ""
Write-Host "Compiling the installer..." -ForegroundColor Cyan
& $iscc $iss
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed."
}

$setup = Get-ChildItem $outDir -Filter 'Usage-Setup-*.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $setup) {
    throw "Inno Setup reported success but no installer was found in $outDir."
}

$hash = (Get-FileHash $setup.FullName -Algorithm SHA256).Hash

Write-Host ""
Write-Host ("Installer ready: {0}" -f $setup.FullName) -ForegroundColor Green
Write-Host ("Size:   {0:N1} MB" -f ($setup.Length / 1MB))
Write-Host ("SHA256: {0}" -f $hash)
Write-Host ""
Write-Host "Attach this file to the GitHub Release and publish the SHA256 beside it."
