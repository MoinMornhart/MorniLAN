<#
.SYNOPSIS
    Build-Skript für MorniLAN.

.EXAMPLE
    ./build.ps1                   # Restore + Build (Release)
    ./build.ps1 -Task Test        # Build + Unit-Tests
    ./build.ps1 -Task Publish     # Self-contained win-x64 Builds aller Apps (lokal: %LOCALAPPDATA%\MorniLAN\publish)
    ./build.ps1 -Task Installer   # Publish + beide Setups (MorniLAN-Geraete-Setup, MorniLAN-Admin-Setup), braucht Inno Setup 6
    ./build.ps1 -Task Installer -OutDir C:\Temp\mornilan   # anderer Ausgabeordner
    ./build.ps1 -Task Clean
#>
[CmdletBinding()]
param(
    [ValidateSet('Build', 'Test', 'Publish', 'Installer', 'Clean')]
    [string]$Task = 'Build',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    # Optionales SemVer-Suffix, z. B. "beta.1"
    [string]$VersionSuffix = '',

    # Optional: anderer Ausgabeordner für Publish/Installer
    [string]$OutDir = ''
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$solution = Join-Path $root 'MorniLAN.slnx'
# Lokal ausserhalb von iCloud ablegen (wie die Build-Ausgaben), auf CI im Repo.
$publishDir = if ($OutDir) { $OutDir } elseif ($env:CI -ne 'true' -and $env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'MorniLAN\publish' } else { Join-Path $root 'publish' }
$installerDir = Join-Path $publishDir 'installer'
$apps = 'MorniLAN.Agent', 'MorniLAN.Launcher', 'MorniLAN.Admin'

function Find-Dotnet {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $userInstall = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
    if (Test-Path $userInstall) { return $userInstall }
    throw '.NET SDK nicht gefunden. Installieren: winget install Microsoft.DotNet.SDK.10'
}

function Find-Iscc {
    $candidates = @(
        $env:ISCC,
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path $_) }
    if ($candidates) { return @($candidates)[0] }
    throw 'Inno Setup 6 nicht gefunden. Installieren: winget install JRSoftware.InnoSetup (oder ISCC-Pfad in $env:ISCC setzen)'
}

function Invoke-Dotnet {
    & $script:dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args[0]) fehlgeschlagen (Exit $LASTEXITCODE)" }
}

# Version wie in den Assemblies: VersionPrefix aus Directory.Build.props plus optionales Suffix.
function Get-AppVersion {
    [xml]$props = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
    $prefix = ($props.Project.PropertyGroup | Where-Object { $_.VersionPrefix } | Select-Object -First 1).VersionPrefix
    if ($VersionSuffix) { "$prefix-$VersionSuffix" } else { $prefix }
}

function Invoke-Publish {
    foreach ($app in $apps) {
        $proj = Join-Path $root "src\$app\$app.csproj"
        $out = Join-Path $publishDir $app
        # Alte Dateien entfernen, damit keine Reste im Installer landen.
        if (Test-Path $out) { Remove-Item $out -Recurse -Force }
        Invoke-Dotnet publish $proj -c $Configuration -r win-x64 --self-contained `
            -p:PublishSingleFile=true -o $out @versionArgs
    }
    Write-Host "==> Ausgabe: $publishDir" -ForegroundColor Green
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
            Invoke-Publish
        }
        'Installer' {
            $iscc = Find-Iscc
            Invoke-Publish
            $version = Get-AppVersion
            New-Item -ItemType Directory -Force $installerDir | Out-Null
            foreach ($script in 'MorniLAN-Geraete.iss', 'MorniLAN-Admin.iss') {
                & $iscc /Q "/DAppVersion=$version" "/DSourceDir=$publishDir" "/DOutputDir=$installerDir" `
                    (Join-Path $root "installer\$script")
                if ($LASTEXITCODE -ne 0) { throw "Inno Setup ($script) fehlgeschlagen (Exit $LASTEXITCODE)" }
            }
            Get-ChildItem $installerDir -Filter "*-$version.exe" |
                ForEach-Object { Write-Host ("==> {0} ({1:N0} MB)" -f $_.Name, ($_.Length / 1MB)) -ForegroundColor Green }
        }
    }
} finally {
    Pop-Location
}

Write-Host '==> Fertig' -ForegroundColor Green
