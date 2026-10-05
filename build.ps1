<#
.SYNOPSIS
    Build-Skript für MorniLAN.

.EXAMPLE
    ./build.ps1                 # Restore + Build (Release)
    ./build.ps1 -Task Test      # Build + Unit-Tests
    ./build.ps1 -Task Publish   # Self-contained win-x64 Builds aller Apps (lokal: %LOCALAPPDATA%\MorniLAN\publish)
    ./build.ps1 -Task Clean
#>
[CmdletBinding()]
param(
    [ValidateSet('Build', 'Test', 'Publish', 'Clean')]
    [string]$Task = 'Build',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    # Optionales SemVer-Suffix, z. B. "beta.1"
    [string]$VersionSuffix = ''
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$solution = Join-Path $root 'MorniLAN.slnx'
# Lokal ausserhalb von iCloud ablegen (wie die Build-Ausgaben), auf CI im Repo.
$publishDir = if ($env:CI -ne 'true' -and $env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'MorniLAN\publish' } else { Join-Path $root 'publish' }
$apps = 'MorniLAN.Agent', 'MorniLAN.Launcher', 'MorniLAN.Admin'

function Find-Dotnet {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $userInstall = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
    if (Test-Path $userInstall) { return $userInstall }
    throw '.NET SDK nicht gefunden. Installieren: winget install Microsoft.DotNet.SDK.10'
}

function Invoke-Dotnet {
    & $script:dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args[0]) fehlgeschlagen (Exit $LASTEXITCODE)" }
}

$dotnet = Find-Dotnet
$versionArgs = if ($VersionSuffix) { @("-p:VersionSuffix=$VersionSuffix") } else { @() }

Write-Host "==> MorniLAN | $Task | $Configuration" -ForegroundColor Cyan

# global.json (SDK-Version, Testplattform) wird nur im Repo-Ordner gefunden.
Push-Location $root
try {
    & $dotnet --version

    switch ($Task) {
        'Clean' {
            Invoke-Dotnet clean $solution -c $Configuration
            if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
        }
        'Build' {
            Invoke-Dotnet build $solution -c $Configuration @versionArgs
        }
        'Test' {
            Invoke-Dotnet build $solution -c $Configuration @versionArgs
            Invoke-Dotnet test --solution $solution -c $Configuration --no-build
        }
        'Publish' {
            foreach ($app in $apps) {
                $proj = Join-Path $root "src\$app\$app.csproj"
                $out = Join-Path $publishDir $app
                Invoke-Dotnet publish $proj -c $Configuration -r win-x64 --self-contained `
                    -p:PublishSingleFile=true -o $out @versionArgs
                # Firewall-Skript mitliefern, damit es auf dem Zielrechner griffbereit ist.
                if ($app -ne 'MorniLAN.Launcher') { Copy-Item (Join-Path $root 'tools\firewall.ps1') $out -Force }
            }
            Write-Host "==> Ausgabe: $publishDir" -ForegroundColor Green
        }
    }
} finally {
    Pop-Location
}

Write-Host '==> Fertig' -ForegroundColor Green
