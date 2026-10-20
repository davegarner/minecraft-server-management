<#
.SYNOPSIS
    Stops and removes the Minecraft Server Manager Windows service.

.DESCRIPTION
    Stopping the service shuts the Minecraft server down gracefully first. Your worlds, backups and
    panel settings are not deleted.

.PARAMETER RemoveFirewallRules
    Also removes the firewall rules created by install-service.ps1.
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$ServiceName = "MinecraftServerManager",
    [switch]$RemoveFirewallRules
)

$ErrorActionPreference = "Stop"

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) {
    Write-Host "Service '$ServiceName' is not installed."
} else {
    if ($service.Status -ne "Stopped") {
        Write-Host "Stopping '$ServiceName' (this stops the Minecraft server gracefully)..."
        Stop-Service -Name $ServiceName
    }
    & sc.exe delete $ServiceName | Out-Null
    Write-Host "Service '$ServiceName' removed." -ForegroundColor Green
}

if ($RemoveFirewallRules) {
    Get-NetFirewallRule -DisplayName "Minecraft Server Manager (TCP *)", "Minecraft Server (TCP *)" -ErrorAction SilentlyContinue |
        Remove-NetFirewallRule
    Write-Host "Firewall rules removed."
}
