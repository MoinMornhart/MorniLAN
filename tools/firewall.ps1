<#
.SYNOPSIS
    Legt die Windows-Firewall-Regeln für MorniLAN an (oder entfernt sie). Braucht Admin-Rechte.

.DESCRIPTION
    Admin (dein PC):     TCP 47950 eingehend – Agents verbinden sich hierher.
                         Erlaubt aus dem lokalen Subnetz (Profil Privat/Domäne) und aus Tailscale (100.64.0.0/10).
    Agent (Freundes-PC): UDP 47951 eingehend – LAN-Beacon des Admin-Panels, nur lokales Subnetz.

    Hat man die Windows-Abfrage "Zugriff gestatten?" für MorniLAN mit "Abbrechen" beantwortet,
    legt Windows Blockier-Regeln für das Programm an. Die gehen vor und werden hier mit entfernt.

.EXAMPLE
    ./tools/firewall.ps1 -Role Admin
    ./tools/firewall.ps1 -Role Agent
    ./tools/firewall.ps1 -Role Admin -Remove
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Admin', 'Agent')]
    [string]$Role,

    [switch]$Remove
)

$ErrorActionPreference = 'Stop'
$group = 'MorniLAN'

$rules = if ($Role -eq 'Admin') {
    @(
        @{ Name = 'MorniLAN Admin (LAN)'; Protocol = 'TCP'; Port = 47950; Profile = 'Private,Domain'; Remote = 'LocalSubnet' }
        @{ Name = 'MorniLAN Admin (Tailscale)'; Protocol = 'TCP'; Port = 47950; Profile = 'Any'; Remote = '100.64.0.0/10' }
    )
} else {
    @(
        @{ Name = 'MorniLAN Agent Suche (LAN)'; Protocol = 'UDP'; Port = 47951; Profile = 'Private,Domain'; Remote = 'LocalSubnet' }
    )
}

foreach ($rule in $rules) {
    Get-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue | Remove-NetFirewallRule
}

# Von Windows automatisch angelegte Blockier-Regeln für MorniLAN-Programme entfernen.
$blocked = Get-NetFirewallApplicationFilter |
    Where-Object { $_.Program -like '*\MorniLAN.*.exe' } |
    Get-NetFirewallRule |
    Where-Object { $_.Action -eq 'Block' -and $_.Direction -eq 'Inbound' }
foreach ($rule in $blocked) {
    Write-Host "    entferne Blockier-Regel: $($rule.DisplayName)" -ForegroundColor Yellow
    $rule | Remove-NetFirewallRule
}

if ($Remove) {
    Write-Host "==> MorniLAN-Regeln ($Role) entfernt" -ForegroundColor Green
    return
}

foreach ($rule in $rules) {
    New-NetFirewallRule -DisplayName $rule.Name -Group $group -Direction Inbound -Action Allow `
        -Protocol $rule.Protocol -LocalPort $rule.Port -Profile $rule.Profile -RemoteAddress $rule.Remote | Out-Null
    Write-Host "    OK  $($rule.Name): $($rule.Protocol) $($rule.Port) von $($rule.Remote)" -ForegroundColor Green
}

$profiles = Get-NetConnectionProfile | Select-Object InterfaceAlias, NetworkCategory
$public = $profiles | Where-Object { $_.NetworkCategory -eq 'Public' -and $_.InterfaceAlias -notlike '*Tailscale*' }
if ($public) {
    Write-Host '    !!  Diese Netzwerke sind als "Öffentlich" eingestuft, dort greift die LAN-Regel nicht:' -ForegroundColor Yellow
    $public | ForEach-Object { Write-Host "        $($_.InterfaceAlias)" -ForegroundColor Yellow }
    Write-Host '        Einstellungen > Netzwerk und Internet > WLAN/Ethernet > Netzwerkprofil "Privat" wählen.' -ForegroundColor Yellow
}
Write-Host "==> Fertig ($Role)" -ForegroundColor Green
