<#
.SYNOPSIS
    Legt die Windows-Firewall-Regeln für MorniLAN an (oder entfernt sie). Braucht Admin-Rechte.

.DESCRIPTION
    Admin (dein PC):     TCP 47950 eingehend – Agents verbinden sich hierher (lokales Subnetz und Tailscale 100.64.0.0/10).
                         UDP 47951 eingehend – Suchanfragen der Agents im LAN (nur lokales Subnetz).
    Agent (Freundes-PC): UDP 47951 eingehend – LAN-Beacon des Admin-Panels (nur lokales Subnetz).

    Die LAN-Regeln gelten für jedes Netzwerkprofil (auch "Öffentlich"), aber nur für Geräte aus dem
    eigenen Subnetz. Die Verbindung selbst ist per TLS und Zertifikat-Pinning geschützt.

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
        @{ Name = 'MorniLAN Admin (LAN)'; Protocol = 'TCP'; Port = 47950; Profile = 'Any'; Remote = 'LocalSubnet' }
        @{ Name = 'MorniLAN Admin (Tailscale)'; Protocol = 'TCP'; Port = 47950; Profile = 'Any'; Remote = '100.64.0.0/10' }
        @{ Name = 'MorniLAN Admin Suche (LAN)'; Protocol = 'UDP'; Port = 47951; Profile = 'Any'; Remote = 'LocalSubnet' }
    )
} else {
    @(
        @{ Name = 'MorniLAN Agent Suche (LAN)'; Protocol = 'UDP'; Port = 47951; Profile = 'Any'; Remote = 'LocalSubnet' }
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

Write-Host "==> Fertig ($Role)" -ForegroundColor Green
