<#
.SYNOPSIS
    End-to-end test of the setup wizard and the MSI: install, upgrade, reconfigure and uninstall.

.DESCRIPTION
    Used by the Release workflow on a fresh Windows runner. It installs services, edits firewall rules and
    deletes C:\ProgramData\Minecraft Server Manager, so only run it on a disposable machine, as administrator.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$SetupExe,
    [Parameter(Mandatory)] [string]$Msi,
    [string]$LogDirectory = "artifacts\test-logs"
)

$ErrorActionPreference = "Stop"
$ServiceName = "MinecraftServerManager"
$InstallDir = Join-Path $env:ProgramFiles "Minecraft Server Manager"
$ConfigDir = Join-Path $env:ProgramData "Minecraft Server Manager"
$Shortcut = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\Minecraft Server Manager.url"

New-Item -ItemType Directory -Force $LogDirectory | Out-Null
$LogDirectory = (Resolve-Path $LogDirectory).Path
$SetupExe = (Resolve-Path $SetupExe).Path
$Msi = (Resolve-Path $Msi).Path
$script:failures = 0

function Check([bool]$Condition, [string]$Message) {
    if ($Condition) { Write-Host "  [ok]   $Message" -ForegroundColor Green }
    else { Write-Host "  [FAIL] $Message" -ForegroundColor Red; $script:failures++ }
}

function Wait-For([scriptblock]$Condition, [int]$Seconds = 60) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        $value = & $Condition
        if ($value) { return $value }
        Start-Sleep -Seconds 1
    }
    return & $Condition
}

function Get-Panel([int]$Port, [string]$Path = "/login") {
    try { Invoke-WebRequest -Uri "http://127.0.0.1:$Port$Path" -UseBasicParsing -TimeoutSec 5 } catch { $null }
}

function Test-Rule([string]$Name) { [bool](Get-NetFirewallRule -DisplayName $Name -ErrorAction SilentlyContinue) }
function Get-Config { Get-Content (Join-Path $ConfigDir "appsettings.json") -Raw | ConvertFrom-Json }
function Get-ServiceStatus { (Get-Service $ServiceName -ErrorAction SilentlyContinue).Status }
function Invoke-Program([string]$File, [string[]]$Arguments) { (Start-Process -FilePath $File -ArgumentList $Arguments -Wait -PassThru).ExitCode }
function Test-RecoveryActions { [bool]((& sc.exe qfailure $ServiceName) -match "RESTART") }

# ------------------------------------------------------------------------------------------------
Write-Host "Setup wizard: fresh install with default options" -ForegroundColor Cyan
$code = Invoke-Program $SetupExe @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/LOG=`"$LogDirectory\setup-install.log`"")
Check ($code -eq 0) "setup exited with code 0 (got $code)"
Check ((Get-ServiceStatus) -eq "Running") "service is running"
Check ((Get-CimInstance Win32_Service -Filter "Name='$ServiceName'").StartMode -eq "Auto") "service starts automatically"
Check (Test-RecoveryActions) "service restarts after a failure"
$page = Wait-For { Get-Panel 8080 "/setup" }
Check ($page -and $page.Content -match "Create account") "panel serves the first-run setup page on port 8080"
Check (Test-Path $Shortcut) "Start menu shortcut created"
Check ((Test-Path $Shortcut) -and ((Get-Content $Shortcut -Raw) -match "URL=http://localhost:8080/")) "shortcut opens http://localhost:8080/"
$config = Get-Config
Check ($config.Urls -eq "http://localhost:8080") "panel listens on localhost only ($($config.Urls))"
Check ($config.Manager.DataDirectory -eq (Join-Path $ConfigDir "data")) "panel data is kept in ProgramData ($($config.Manager.DataDirectory))"
Check (Test-Rule "Minecraft Server (TCP 25565)") "firewall rule lets Minecraft players in"
Check (-not (Test-Rule "Minecraft Server Manager (TCP 8080)")) "no firewall rule for the panel by default"

# Complete first-run setup so the upgrade below can prove that settings survive.
$form = Invoke-WebRequest -Uri "http://127.0.0.1:8080/setup" -UseBasicParsing -SessionVariable session
$token = [regex]::Match($form.Content, 'name="__RequestVerificationToken" value="([^"]+)"').Groups[1].Value
$null = Invoke-WebRequest -Uri "http://127.0.0.1:8080/setup" -Method Post -WebSession $session -UseBasicParsing -Body @{
    "_handler" = "setup"; "__RequestVerificationToken" = $token; "Input.Username" = "admin"
    "Input.Password" = "ci-only-password"; "Input.ConfirmPassword" = "ci-only-password"; "Input.ServerDirectory" = "C:\MinecraftServerCI"
}
Check (Test-Path (Join-Path $ConfigDir "data\settings.json")) "first-run setup completed"

# ------------------------------------------------------------------------------------------------
Write-Host "Setup wizard: upgrade in place" -ForegroundColor Cyan
$code = Invoke-Program $SetupExe @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/PORT=9999", "/LOG=`"$LogDirectory\setup-upgrade.log`"")
Check ($code -eq 0) "upgrade exited with code 0 (got $code)"
Check ((Get-ServiceStatus) -eq "Running") "service is running after the upgrade"
$page = Wait-For { Get-Panel 8080 "/login" }
Check ($page -and $page.Content -match "Sign in") "panel kept its port and account (shows sign-in, not setup)"
Check ((Get-Config).Urls -eq "http://localhost:8080") "upgrade didn't change the panel address"

# ------------------------------------------------------------------------------------------------
Write-Host "configure command" -ForegroundColor Cyan
$exe = Join-Path $InstallDir "MinecraftServerManager.exe"
Check ((Invoke-Program $exe @("configure", "--port", "8085")) -eq 0) "configure --port 8085 succeeded"
Restart-Service $ServiceName
Check ([bool](Wait-For { Get-Panel 8085 })) "panel answers on port 8085 after a restart"
Check ((Invoke-Program $exe @("configure", "--port", "8080")) -eq 0) "configure --port 8080 succeeded"
Restart-Service $ServiceName
Check ([bool](Wait-For { Get-Panel 8080 })) "panel back on port 8080"

# ------------------------------------------------------------------------------------------------
Write-Host "Setup wizard: uninstall" -ForegroundColor Cyan
$code = Invoke-Program (Join-Path $InstallDir "unins000.exe") @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/LOG=`"$LogDirectory\setup-uninstall.log`"")
Check ($code -eq 0) "uninstaller exited with code 0 (got $code)"
Check ([bool](Wait-For { -not (Get-Service $ServiceName -ErrorAction SilentlyContinue) })) "service removed"
Check ([bool](Wait-For { -not (Test-Path (Join-Path $InstallDir "MinecraftServerManager.exe")) })) "program files removed"
Check (-not (Test-Rule "Minecraft Server (TCP 25565)")) "firewall rule removed"
Check (-not (Test-Path $Shortcut)) "Start menu shortcut removed"
Check (Test-Path (Join-Path $ConfigDir "data\settings.json")) "panel settings kept (silent uninstall)"

# ------------------------------------------------------------------------------------------------
Write-Host "MSI: install on a clean machine with PANELPORT=8090 REMOTEACCESS=1" -ForegroundColor Cyan
Remove-Item $ConfigDir -Recurse -Force
$code = Invoke-Program "msiexec.exe" @("/i", "`"$Msi`"", "/qn", "/norestart", "/l*v", "`"$LogDirectory\msi-install.log`"", "PANELPORT=8090", "REMOTEACCESS=1")
Check ($code -eq 0) "msiexec install exited with code 0 (got $code)"
Check ((Get-ServiceStatus) -eq "Running") "service is running"
Check (Test-RecoveryActions) "service restarts after a failure"
$page = Wait-For { Get-Panel 8090 "/setup" }
Check ($page -and $page.Content -match "Create account") "panel serves the first-run setup page on port 8090"
Check ((Get-Config).Urls -eq "http://0.0.0.0:8090") "panel listens on the network on port 8090"
Check (Test-Rule "Minecraft Server (TCP 25565)") "firewall rule lets Minecraft players in"
Check (Test-Rule "Minecraft Server Manager (TCP 8090)") "firewall rule lets other computers reach the panel"
Check ((Test-Path $Shortcut) -and ((Get-Content $Shortcut -Raw) -match "localhost:8090")) "Start menu shortcut opens port 8090"

Write-Host "MSI: uninstall" -ForegroundColor Cyan
$code = Invoke-Program "msiexec.exe" @("/x", "`"$Msi`"", "/qn", "/norestart", "/l*v", "`"$LogDirectory\msi-uninstall.log`"")
Check ($code -eq 0) "msiexec uninstall exited with code 0 (got $code)"
Check (-not (Get-Service $ServiceName -ErrorAction SilentlyContinue)) "service removed"
Check (-not (Test-Path (Join-Path $InstallDir "MinecraftServerManager.exe"))) "program files removed"
Check (-not (Test-Rule "Minecraft Server (TCP 25565)") -and -not (Test-Rule "Minecraft Server Manager (TCP 8090)")) "firewall rules removed"
Check (-not (Test-Path $Shortcut)) "Start menu shortcut removed"

# ------------------------------------------------------------------------------------------------
if ($script:failures) { throw "$($script:failures) installer check(s) failed. Logs: $LogDirectory" }
Write-Host "All installer checks passed." -ForegroundColor Green
