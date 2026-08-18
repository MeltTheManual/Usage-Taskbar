$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'dist'
dotnet publish (Join-Path $root 'src\Usage.App\Usage.App.csproj') -c Release -r win-x64 --self-contained false -o $out
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed."
}
Write-Host "Published $out\Usage.exe"
