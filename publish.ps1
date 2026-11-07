# Builds a self-contained single-file x64 release of the app into .\dist
# Usage (normal terminal is fine):  .\publish.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'dist'

dotnet publish (Join-Path $root 'src\HotasController.App\HotasController.App.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None `
    -o $out
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

Write-Host "`nPublished to $out"
Get-ChildItem $out | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}} | Format-Table -AutoSize
Write-Host "Run $out\HotasController.App.exe (it asks for administrator rights)."
