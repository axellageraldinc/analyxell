<#
.SYNOPSIS
  Builds the two release artifacts: a portable zip and an MSIX installer.

.PARAMETER Version
  Version label used in file names, for example 0.1.0.

.PARAMETER CertPath
  Optional .pfx used to sign the MSIX. When omitted a throw-away development certificate is generated under
  artifacts\certs (git-ignored). A development certificate is NOT trusted on other PCs; for public releases
  sign with a certificate you control, via -CertPath.

.PARAMETER CertPassword
  Password of -CertPath. Defaults to the password of the generated development certificate.

.EXAMPLE
  pwsh -File scripts\package.ps1 -Version 0.1.0
#>
param(
    [string]$Version = '0.3.1',
    [string]$CertPath = '',
    [string]$CertPassword = 'password',
    [ValidateSet('x64', 'arm64')]
    [string]$Arch = 'x64'
)

$ErrorActionPreference = 'Stop'
$env:WINAPP_CLI_TELEMETRY_OPTOUT = '1'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\Analyxell\Analyxell.csproj'
$out = Join-Path $root 'artifacts'
$rid = "win-$Arch"

# Start from clean Release outputs so nothing built earlier with debug symbols (and their paths) is reused.
foreach ($dir in @('src\Analyxell.Core\obj', 'src\Analyxell.Corein', 'src\Analyxell\obj', 'src\Analyxellin')) {
    Get-ChildItem (Join-Path $root $dir) -Directory -Filter 'Release*' -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    Get-ChildItem (Join-Path $root $dir) -Directory -ErrorAction SilentlyContinue | ForEach-Object { Get-ChildItem $_.FullName -Directory -Filter 'Release*' -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue }
}

New-Item -ItemType Directory -Force $out | Out-Null

# 1. Portable: unpackaged, self-contained folder, zipped. No installation and no certificate needed.
$portableDir = Join-Path $out "portable\$rid"
Remove-Item $portableDir -Recurse -Force -ErrorAction SilentlyContinue
# No debug symbols in releases: they (and the paths they embed in the DLLs) would carry the build machine's folder names.
dotnet publish $project -c Release -r $rid "-p:Platform=$Arch" -p:Portable=true -p:DebugType=none -p:DebugSymbols=false -o $portableDir -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed.' }
$zip = Join-Path $out "analyxell-$Version-portable-$rid.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $portableDir '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Portable: $zip"

# 2. Installer: signed MSIX.
if (-not $CertPath) {
    $certDir = Join-Path $out 'certs'
    New-Item -ItemType Directory -Force $certDir | Out-Null
    $CertPath = Join-Path $certDir 'devcert.pfx'
    if (-not (Test-Path $CertPath)) {
        Push-Location $certDir
        try {
            winapp cert generate --manifest (Join-Path $root 'src\Analyxell\Package.appxmanifest') --if-exists skip
        }
        finally { Pop-Location }
    }
}

$msix = Join-Path $out "analyxell-$Version-$rid.msix"
Remove-Item $msix -ErrorAction SilentlyContinue

# Self-contained so the installer works on a PC without the .NET or Windows App SDK runtimes installed.
winapp package $project --arch $Arch --cert $CertPath --cert-password $CertPassword --output $msix `
    --self-contained -p SelfContained=true -p DebugType=none -p DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'MSIX packaging failed.' }
Write-Host "Installer: $msix"
