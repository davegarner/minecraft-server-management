<#
.SYNOPSIS
    Installs or updates Minecraft Server Manager as a Windows service that starts with Windows.

.DESCRIPTION
    Run from an elevated PowerShell prompt in the folder that contains MinecraftServerManager.exe.
    The service runs as its own virtual account (NT SERVICE\MinecraftServerManager), restarts automatically
    if it fails, and stops the Minecraft server gracefully when Windows shuts down. The script gives that
    account access to the panel's folders and stops other users from changing them. Running it again
    updates the existing service, so it's safe to use after copying a new release over an old one. The
    setup wizard runs it for you.

.PARAMETER Account
    The account the service runs as. Defaults to NT SERVICE\<ServiceName>, or for an existing service to the
    account it already uses unless that is LocalSystem. "LocalSystem" restores the behaviour of versions
    before 1.0.3. Another account needs a password set separately and access to Java and the server folders.

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
    [string]$Account,
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

# Run as the service's own virtual account (no password), then let it use the panel's folders. An account an
# administrator chose is kept; Local System, the default before 1.0.3, is replaced.
$current = (Get-CimInstance Win32_Service -Filter "Name='$ServiceName'").StartName
if (-not $Account) { $Account = if ($current -and $current -ne "LocalSystem") { $current } else { "NT SERVICE\$ServiceName" } }
if ($Account -ne $current) {
    & sc.exe config $ServiceName obj= $Account | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Couldn't set the service to run as $Account (sc.exe exit code $LASTEXITCODE)." }
}
Write-Host "The service runs as $Account."
& $exe secure --service-name $ServiceName
if ($LASTEXITCODE -ne 0) { throw "Couldn't set the folder permissions (exit code $LASTEXITCODE)." }

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
