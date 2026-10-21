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
    # Optional: a higher-versioned build of the same MSI, to test that an upgrade keeps the options.
    [string]$UpgradeMsi,
    # Optional: the previous release's setup wizard, to test upgrading an existing installation.
    [string]$PreviousSetupExe,
    [string]$LogDirectory = "artifacts\test-logs"
)

$ErrorActionPreference = "Stop"
$ServiceName = "MinecraftServerManager"
$ServiceAccount = "NT SERVICE\$ServiceName"
$InstallDir = Join-Path $env:ProgramFiles "Minecraft Server Manager"
$ConfigDir = Join-Path $env:ProgramData "Minecraft Server Manager"
$Shortcut = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\Minecraft Server Manager.url"

New-Item -ItemType Directory -Force $LogDirectory | Out-Null
$LogDirectory = (Resolve-Path $LogDirectory).Path
$SetupExe = (Resolve-Path $SetupExe).Path
$Msi = (Resolve-Path $Msi).Path
if ($UpgradeMsi) { $UpgradeMsi = (Resolve-Path $UpgradeMsi).Path }
if ($PreviousSetupExe) { $PreviousSetupExe = (Resolve-Path $PreviousSetupExe).Path }
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
# Runs an installer and returns its exit code. A run that hangs is stopped after $TimeoutMinutes and counted as a
# failure, so the job still finishes and uploads the installer logs.
function Invoke-Program([string]$File, [string[]]$Arguments, [int]$TimeoutMinutes = 10) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -PassThru
    $null = $process.Handle # keeps the exit code available after the process ends
    if ($process.WaitForExit($TimeoutMinutes * 60 * 1000)) { return $process.ExitCode }

    Write-Host "  [FAIL] $(Split-Path $File -Leaf) $($Arguments -join ' ') didn't finish within $TimeoutMinutes minutes" -ForegroundColor Red
    $script:failures++
    Get-Process msiexec, MinecraftServerManager, java -ErrorAction SilentlyContinue | Format-Table Id, ProcessName, StartTime | Out-String | Write-Host
    Get-Service $ServiceName -ErrorAction SilentlyContinue | Format-List Name, Status, StartType | Out-String | Write-Host
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    return -1
}
function Test-RecoveryActions { [bool]((& sc.exe qfailure $ServiceName) -match "RESTART") }
function Get-ServiceAccount { (Get-CimInstance Win32_Service -Filter "Name='$ServiceName'").StartName }

# Only SYSTEM, Administrators and the service's account have access, and nothing is inherited from the parent.
function Test-Private([string]$Path) {
    $acl = Get-Acl $Path
    $others = $acl.Access | Where-Object { $_.IdentityReference.Value -notin @("NT AUTHORITY\SYSTEM", "BUILTIN\Administrators", $ServiceAccount) }
    if ($others) { Write-Host "    $Path also grants: $(($others.IdentityReference.Value | Sort-Object -Unique) -join ', ')" }
    $acl.AreAccessRulesProtected -and -not $others
}

function Complete-Setup([int]$Port, [string]$ServerDirectory) {
    $form = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/setup" -UseBasicParsing -SessionVariable session
    $token = [regex]::Match($form.Content, 'name="__RequestVerificationToken" value="([^"]+)"').Groups[1].Value
    $null = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/setup" -Method Post -WebSession $session -UseBasicParsing -Body @{
        "_handler" = "setup"; "__RequestVerificationToken" = $token; "Input.Username" = "admin"
        "Input.Password" = "ci-only-password"; "Input.ConfirmPassword" = "ci-only-password"; "Input.ServerDirectory" = $ServerDirectory
    }
}

# Signing in needs the service to read settings.json and the sign-in keys, and to issue a cookie.
function Test-SignIn([int]$Port) {
    try {
        $form = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/login" -UseBasicParsing -SessionVariable session
        $token = [regex]::Match($form.Content, 'name="__RequestVerificationToken" value="([^"]+)"').Groups[1].Value
        $page = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/login" -Method Post -WebSession $session -UseBasicParsing -Body @{
            "_handler" = "login"; "__RequestVerificationToken" = $token; "Input.Username" = "admin"; "Input.Password" = "ci-only-password"
        }
        $page.Content -match "<title>Dashboard"
    } catch { $false }
}

# ------------------------------------------------------------------------------------------------
if ($PreviousSetupExe) {
    Write-Host "Setup wizard: upgrade an installation of the previous release" -ForegroundColor Cyan
    $code = Invoke-Program $PreviousSetupExe @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/LOG=`"$LogDirectory\previous-install.log`"")
    Check ($code -eq 0) "previous release installed (exit code $code)"
    Check ([bool](Wait-For { Get-Panel 8080 "/setup" })) "previous release serves the setup page"
    Complete-Setup 8080 "C:\MinecraftServerPrevious"
    Check (Test-SignIn 8080) "can sign in to the previous release"

    $code = Invoke-Program $SetupExe @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/LOG=`"$LogDirectory\previous-upgrade.log`"")
    Check ($code -eq 0) "upgrade exited with code 0 (got $code)"
    Check ((Get-ServiceAccount) -eq $ServiceAccount) "service now runs as $ServiceAccount ($(Get-ServiceAccount))"
    Check ([bool](Wait-For { Get-Panel 8080 "/login" })) "panel answers after the upgrade"
    Check (Test-SignIn 8080) "the same account can still sign in (settings and keys readable)"
    Check (Test-Private (Join-Path $ConfigDir "data")) "panel data limited to SYSTEM, Administrators and the service"
    Check (Test-Private "C:\MinecraftServerPrevious") "existing server folder no longer writable by other users"

    # Back to a clean machine for the checks below.
    $null = Invoke-Program (Join-Path $InstallDir "unins000.exe") @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART")
    $null = Wait-For { -not (Get-Service $ServiceName -ErrorAction SilentlyContinue) }
    Remove-Item $ConfigDir, "C:\MinecraftServerPrevious" -Recurse -Force -ErrorAction SilentlyContinue
}

# ------------------------------------------------------------------------------------------------
Write-Host "Setup wizard: fresh install with default options" -ForegroundColor Cyan
$code = Invoke-Program $SetupExe @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/LOG=`"$LogDirectory\setup-install.log`"")
Check ($code -eq 0) "setup exited with code 0 (got $code)"
Check ((Get-ServiceStatus) -eq "Running") "service is running"
Check ((Get-CimInstance Win32_Service -Filter "Name='$ServiceName'").StartMode -eq "Auto") "service starts automatically"
Check (Test-RecoveryActions) "service restarts after a failure"
Check ((Get-ServiceAccount) -eq $ServiceAccount) "service runs as $ServiceAccount ($(Get-ServiceAccount))"
$page = Wait-For { Get-Panel 8080 "/setup" }
Check ($page -and $page.Content -match "Create account") "panel serves the first-run setup page on port 8080"
Check (Test-Private (Join-Path $ConfigDir "data")) "panel data limited to SYSTEM, Administrators and the service"
Check ([System.Diagnostics.EventLog]::SourceExists($ServiceName)) "Event Log source registered"
Check (Test-Path $Shortcut) "Start menu shortcut created"
Check ((Test-Path $Shortcut) -and ((Get-Content $Shortcut -Raw) -match "URL=http://localhost:8080/")) "shortcut opens http://localhost:8080/"
$config = Get-Config
Check ($config.Urls -eq "http://localhost:8080") "panel listens on localhost only ($($config.Urls))"
Check ($config.Manager.DataDirectory -eq (Join-Path $ConfigDir "data")) "panel data is kept in ProgramData ($($config.Manager.DataDirectory))"
Check (Test-Rule "Minecraft Server (TCP 25565)") "firewall rule lets Minecraft players in"
Check (-not (Test-Rule "Minecraft Server Manager (TCP 8080)")) "no firewall rule for the panel by default"

# Complete first-run setup so the upgrade below can prove that settings survive.
Complete-Setup 8080 "C:\MinecraftServerCI"
Check (Test-Path (Join-Path $ConfigDir "data\settings.json")) "first-run setup completed"
Check (Test-SignIn 8080) "can sign in with the new account"
Check (Test-Private "C:\MinecraftServerCI") "server folder created at setup isn't writable by other users"

# ------------------------------------------------------------------------------------------------
Write-Host "Setup wizard: upgrade in place" -ForegroundColor Cyan
$code = Invoke-Program $SetupExe @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/PORT=9999", "/LOG=`"$LogDirectory\setup-upgrade.log`"")
Check ($code -eq 0) "upgrade exited with code 0 (got $code)"
Check ((Get-ServiceStatus) -eq "Running") "service is running after the upgrade"
$page = Wait-For { Get-Panel 8080 "/login" }
Check ($page -and $page.Content -match "Sign in") "panel kept its port and account (shows sign-in, not setup)"
Check ((Get-Config).Urls -eq "http://localhost:8080") "upgrade didn't change the panel address"
Check ((Get-ServiceAccount) -eq $ServiceAccount) "service still runs as $ServiceAccount"
Check (Test-SignIn 8080) "can still sign in after the upgrade"

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
Check ((Get-ServiceAccount) -eq $ServiceAccount) "service runs as $ServiceAccount ($(Get-ServiceAccount))"
$page = Wait-For { Get-Panel 8090 "/setup" }
Check ($page -and $page.Content -match "Create account") "panel serves the first-run setup page on port 8090"
Check (Test-Private (Join-Path $ConfigDir "data")) "panel data limited to SYSTEM, Administrators and the service"
Check ((Get-Config).Urls -eq "http://0.0.0.0:8090") "panel listens on the network on port 8090"
Check (Test-Rule "Minecraft Server (TCP 25565)") "firewall rule lets Minecraft players in"
Check (Test-Rule "Minecraft Server Manager (TCP 8090)") "firewall rule lets other computers reach the panel"
Check ((Test-Path $Shortcut) -and ((Get-Content $Shortcut -Raw) -match "localhost:8090")) "Start menu shortcut opens port 8090"

$installedMsi = $Msi
if ($UpgradeMsi) {
    Write-Host "MSI: upgrade without options keeps the previous choices" -ForegroundColor Cyan
    $code = Invoke-Program "msiexec.exe" @("/i", "`"$UpgradeMsi`"", "/qn", "/norestart", "/l*v", "`"$LogDirectory\msi-upgrade.log`"")
    Check ($code -eq 0) "msiexec upgrade exited with code 0 (got $code)"
    Check ((Get-ServiceStatus) -eq "Running") "service is running after the upgrade"
    Check ([bool](Wait-For { Get-Panel 8090 })) "panel still answers on port 8090"
    Check ((Get-Config).Urls -eq "http://0.0.0.0:8090") "upgrade didn't change the panel address"
    Check (Test-Rule "Minecraft Server Manager (TCP 8090)") "panel firewall rule kept"
    Check (Test-Rule "Minecraft Server (TCP 25565)") "Minecraft firewall rule kept"
    Check ((Test-Path $Shortcut) -and ((Get-Content $Shortcut -Raw) -match "localhost:8090")) "Start menu shortcut still opens port 8090"
    $installedMsi = $UpgradeMsi
}

Write-Host "MSI: uninstall" -ForegroundColor Cyan
$code = Invoke-Program "msiexec.exe" @("/x", "`"$installedMsi`"", "/qn", "/norestart", "/l*v", "`"$LogDirectory\msi-uninstall.log`"")
Check ($code -eq 0) "msiexec uninstall exited with code 0 (got $code)"
Check (-not (Get-Service $ServiceName -ErrorAction SilentlyContinue)) "service removed"
Check (-not (Test-Path (Join-Path $InstallDir "MinecraftServerManager.exe"))) "program files removed"
Check (-not (Test-Rule "Minecraft Server (TCP 25565)") -and -not (Test-Rule "Minecraft Server Manager (TCP 8090)")) "firewall rules removed"
Check (-not (Test-Path $Shortcut)) "Start menu shortcut removed"

# ------------------------------------------------------------------------------------------------
if ($script:failures) { throw "$($script:failures) installer check(s) failed. Logs: $LogDirectory" }
Write-Host "All installer checks passed." -ForegroundColor Green
