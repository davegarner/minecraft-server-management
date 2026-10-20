<#
.SYNOPSIS
    Installs Minecraft Server Manager as a Windows service that starts with Windows.

.DESCRIPTION
    Run from an elevated PowerShell prompt in the folder that contains MinecraftServerManager.exe.
    The service runs as LocalSystem, restarts automatically if it fails, and stops the Minecraft
    server gracefully when Windows shuts down.

.PARAMETER OpenPanelPort
    Adds a Windows Firewall rule for the web panel port. Only needed for access from other machines;
    also set "Urls" in appsettings.json to listen on the network (see README).

.PARAMETER OpenMinecraftPort
    Adds a Windows Firewall rule so players can connect to the Minecraft server.

.EXAMPLE
    .\install-service.ps1 -OpenMinecraftPort
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$ServiceName = "MinecraftServerManager",
    [string]$DisplayName = "Minecraft Server Manager",
    [int]$PanelPort = 8080,
    [int]$MinecraftPort = 25565,
    [switch]$OpenPanelPort,
    [switch]$OpenMinecraftPort
)

$ErrorActionPreference = "Stop"

$exe = Join-Path $PSScriptRoot "MinecraftServerManager.exe"
if (-not (Test-Path $exe)) {
    throw "MinecraftServerManager.exe was not found in $PSScriptRoot. Run this script from the published folder."
}
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    throw "A service named '$ServiceName' already exists. Run uninstall-service.ps1 first."
}

New-Service -Name $ServiceName `
    -BinaryPathName "`"$exe`"" `
    -DisplayName $DisplayName `
    -Description "Web control panel for a Minecraft Java Edition server." `
    -StartupType Automatic | Out-Null

# Restart the service after 1 minute if it ever fails (reset the failure count daily).
& sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

if ($OpenPanelPort) {
    New-NetFirewallRule -DisplayName "Minecraft Server Manager (TCP $PanelPort)" -Direction Inbound `
        -Protocol TCP -LocalPort $PanelPort -Action Allow | Out-Null
    Write-Host "Firewall: opened TCP $PanelPort for the web panel."
}
if ($OpenMinecraftPort) {
    New-NetFirewallRule -DisplayName "Minecraft Server (TCP $MinecraftPort)" -Direction Inbound `
        -Protocol TCP -LocalPort $MinecraftPort -Action Allow | Out-Null
    Write-Host "Firewall: opened TCP $MinecraftPort for Minecraft players."
}

Start-Service -Name $ServiceName
Write-Host "Service '$ServiceName' installed and started." -ForegroundColor Green
Write-Host "Open the panel at the URL set in appsettings.json (default: http://localhost:$PanelPort)."
