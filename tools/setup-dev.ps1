<#
.SYNOPSIS
    Richtet einen Entwicklungsrechner für MorniLAN ein (z. B. den Laptop).

.DESCRIPTION
    - prüft Git und freien Speicherplatz
    - installiert das .NET 10 SDK, falls es fehlt (winget, sonst ohne Admin-Rechte ins Benutzerprofil)
    - setzt die Git-Identität für dieses Repo (GitHub-noreply-Adresse)
    - baut die Projektmappe und führt die Tests aus
    - zeigt an, ob Hyper-V für die Test-VM aktiv ist

.EXAMPLE
    ./tools/setup-dev.ps1
    ./tools/setup-dev.ps1 -NoInstall -SkipBuild    # nur prüfen
#>
[CmdletBinding()]
param(
    [switch]$NoInstall,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }
function Ok($text) { Write-Host "    OK  $text" -ForegroundColor Green }
function Warn($text) { Write-Host "    !!  $text" -ForegroundColor Yellow }

function Get-Dotnet10 {
    $candidates = @()
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { $candidates += $cmd.Source }
    $candidates += Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
    foreach ($exe in $candidates) {
        if (-not (Test-Path $exe)) { continue }
        $sdks = & $exe --list-sdks 2>$null
        if ($sdks | Where-Object { $_ -match '^10\.' }) { return $exe }
    }
    return $null
}

# --- Git -------------------------------------------------------------------
Step 'Git'
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git fehlt. Installieren: winget install Git.Git'
}
Ok (git --version)

Push-Location $root
try {
    if (-not (git config --local user.email)) {
        git config --local user.name 'MoinMornhart'
        git config --local user.email '297179352+MoinMornhart@users.noreply.github.com'
        Ok 'Git-Identität für dieses Repo gesetzt (noreply)'
    } else {
        Ok "Git-Identität: $(git config user.name) <$(git config user.email)>"
    }
    $branch = git rev-parse --abbrev-ref HEAD
    Ok "Branch: $branch"
} finally { Pop-Location }

if (Get-Command gh -ErrorAction SilentlyContinue) { Ok 'GitHub CLI vorhanden' }
else { Warn 'GitHub CLI fehlt (optional): winget install GitHub.cli' }

# --- Speicherplatz ---------------------------------------------------------
Step 'Speicherplatz'
$freeGb = [math]::Round((Get-PSDrive C).Free / 1GB, 1)
if ($freeGb -lt 5) { Warn "Nur $freeGb GB frei auf C:. Für SDK + Builds mindestens 5 GB, für die Test-VM zusätzlich 40-60 GB." }
else { Ok "$freeGb GB frei auf C:" }

# --- .NET SDK --------------------------------------------------------------
Step '.NET 10 SDK'
$dotnet = Get-Dotnet10
if (-not $dotnet -and -not $NoInstall) {
    Warn 'Nicht gefunden, versuche winget ...'
    winget install --id Microsoft.DotNet.SDK.10 -e --accept-package-agreements --accept-source-agreements
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
    $dotnet = Get-Dotnet10

    if (-not $dotnet) {
        Warn 'winget hat nicht geklappt, installiere ohne Admin-Rechte ins Benutzerprofil ...'
        $installDir = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
        $script = Join-Path $env:TEMP 'dotnet-install.ps1'
        Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $script -UseBasicParsing
        & $script -Channel 10.0 -InstallDir $installDir -NoPath
        $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
        if ($userPath -notlike "*$installDir*") {
            [Environment]::SetEnvironmentVariable('Path', "$installDir;$userPath", 'User')
        }
        [Environment]::SetEnvironmentVariable('DOTNET_ROOT', $installDir, 'User')
        $env:Path = "$installDir;$env:Path"
        $dotnet = Get-Dotnet10
    }
}
if ($dotnet) { Ok "$(& $dotnet --version) ($dotnet)" }
else { Warn '.NET 10 SDK fehlt. Manuell installieren: winget install Microsoft.DotNet.SDK.10'; $SkipBuild = $true }

# --- Hyper-V (für die Test-VM) ---------------------------------------------
Step 'Hyper-V'
try {
    $hv = Get-CimInstance Win32_OptionalFeature -Filter "Name='Microsoft-Hyper-V-All'" -ErrorAction Stop
    if ($hv.InstallState -eq 1) { Ok 'Hyper-V ist aktiviert' }
    else { Warn 'Hyper-V ist nicht aktiviert (für die Win11-Test-VM nötig, Windows Pro erforderlich).' }
} catch { Warn 'Hyper-V-Status konnte nicht ermittelt werden.' }

# --- Build & Tests ---------------------------------------------------------
if (-not $SkipBuild) {
    Step 'Build & Tests'
    & (Join-Path $root 'build.ps1') -Task Test
}

Step 'Fertig. Nächste Schritte: siehe CLAUDE.md (Abschnitt "Stand").'
