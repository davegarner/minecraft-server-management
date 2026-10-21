<#
.SYNOPSIS
    Installs or updates Minecraft Server Manager as a Windows service that starts with Windows.

.DESCRIPTION
    Run from an elevated PowerShell prompt in the folder that contains MinecraftServerManager.exe.
    The service runs as LocalSystem, restarts automatically if it fails, and stops the Minecraft
    server gracefully when Windows shuts down. Running the script again updates the existing service,
    so it's safe to use after copying a new release over an old one. The setup wizard runs it for you.

.PARAMETER OpenPanelPort
    Adds a Windows Firewall rule for the web panel port. Only needed for access from other machines;
    the panel must also listen on the network (see the wiki page "Remote Access and HTTPS").

.PARAMETER OpenMinecraftPort
    Adds a Windows Firewall rule so players can connect to the Minecraft server.

.PARAMETER WaitForPanel
    Waits up to 60 seconds for the panel to answer on http://127.0.0.1:<PanelPort>.

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
    [switch]$OpenMinecraftPort,
    [switch]$WaitForPanel
)

$ErrorActionPreference = "Stop"
$description = "Web control panel for a Minecraft Java Edition server."

$exe = Join-Path $PSScriptRoot "MinecraftServerManager.exe"
if (-not (Test-Path $exe)) {
    throw "MinecraftServerManager.exe was not found in $PSScriptRoot. Run this script from the panel's folder."
}
$binaryPath = "`"$exe`""

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne "Stopped") { Stop-Service -Name $ServiceName }
    Set-Service -Name $ServiceName -DisplayName $DisplayName -Description $description -StartupType Automatic
    # Set-Service can't change the executable path in Windows PowerShell 5.1, so use WMI.
    $result = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'" |
        Invoke-CimMethod -MethodName Change -Arguments @{ PathName = $binaryPath }
    if ($result.ReturnValue -ne 0) { throw "Couldn't update the service's executable path (error $($result.ReturnValue))." }
    Write-Host "Updated the existing '$ServiceName' service."
} else {
    New-Service -Name $ServiceName -BinaryPathName $binaryPath -DisplayName $DisplayName `
        -Description $description -StartupType Automatic | Out-Null
    Write-Host "Created the '$ServiceName' service."
}

# Restart the service after 1 minute if it ever fails (reset the failure count daily).
& sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

function Set-InboundRule([string]$Name, [int]$Port) {
    Get-NetFirewallRule -DisplayName $Name -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    New-NetFirewallRule -DisplayName $Name -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null
    Write-Host "Firewall: allowed inbound TCP $Port ($Name)."
}
if ($OpenPanelPort) { Set-InboundRule "Minecraft Server Manager (TCP $PanelPort)" $PanelPort }
if ($OpenMinecraftPort) { Set-InboundRule "Minecraft Server (TCP $MinecraftPort)" $MinecraftPort }

Start-Service -Name $ServiceName
Write-Host "Service '$ServiceName' is running." -ForegroundColor Green

if ($WaitForPanel) {
    $url = "http://127.0.0.1:$PanelPort/login"
    $deadline = (Get-Date).AddSeconds(60)
    while ($true) {
        try {
            Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5 | Out-Null
            Write-Host "The panel is answering on port $PanelPort."
            break
        } catch {
            if ((Get-Date) -gt $deadline) {
                Write-Warning "The panel didn't answer on $url within 60 seconds. Check the Windows Event Log (Application)."
                break
            }
            Start-Sleep -Seconds 1
        }
    }
}

Write-Host "Open the panel at http://localhost:$PanelPort (or the address set with 'MinecraftServerManager.exe configure')."
