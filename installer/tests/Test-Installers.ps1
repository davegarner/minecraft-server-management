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

# In PowerShell 7.4+ -TimeoutSec only limits connecting, so also limit the wait for a response; a panel that
# accepts a connection but never answers must fail a check, not hang the run.
$web = @{ UseBasicParsing = $true; TimeoutSec = 10 }
if ((Get-Command Invoke-WebRequest).Parameters.ContainsKey("OperationTimeoutSeconds")) { $web.OperationTimeoutSeconds = 30 }

function Get-Panel([int]$Port, [string]$Path = "/login") {
    try { Invoke-WebRequest -Uri "http://127.0.0.1:$Port$Path" @web } catch { $null }
}

# Prints what explains an installer failure: the installer log around the error, the service's state and the
# recent Service Control Manager and application events.
function Write-Diagnostics([string]$Log) {
    Write-Host "  ---- diagnostics ----" -ForegroundColor Yellow
    if ($Log -and (Test-Path $Log)) {
        $lines = @(Get-Content $Log)
        $failed = ($lines | Select-String -Pattern "Return value 3" | Select-Object -First 1).LineNumber
        $from = if ($failed) { [Math]::Max(0, $failed - 30) } else { [Math]::Max(0, $lines.Count - 30) }
        $lines[$from..([Math]::Min($lines.Count - 1, $from + 35))] | ForEach-Object { Write-Host "  | $_" }
        $lines | Select-String -Pattern "Error 19\d\d|Error 1603|failed to (start|stop)|could not be stopped" |
            Select-Object -First 15 | ForEach-Object { Write-Host "  ! $($_.Line)" }
    }
    Get-Process msiexec, MinecraftServerManager, java -ErrorAction SilentlyContinue | Format-Table Id, ProcessName, StartTime | Out-String | Write-Host
    # sc.exe rather than WMI: a WMI query hung here while Windows Installer was in a bad state.
    & sc.exe queryex $ServiceName | Out-String | Write-Host
    $since = (Get-Date).AddMinutes(-15)
    Get-WinEvent -FilterHashtable @{ LogName = "System"; ProviderName = "Service Control Manager"; StartTime = $since } -MaxEvents 200 -ErrorAction SilentlyContinue |
        Where-Object Message -match "Minecraft" | Select-Object -First 20 |
        ForEach-Object { Write-Host "  [SCM $($_.TimeCreated.ToString('HH:mm:ss'))] $($_.Message)" }
    Get-WinEvent -FilterHashtable @{ LogName = "Application"; StartTime = $since } -MaxEvents 300 -ErrorAction SilentlyContinue |
        Where-Object { $_.ProviderName -in "MinecraftServerManager", ".NET Runtime", "Application Error", "MsiInstaller" } |
        Select-Object -First 30 |
        ForEach-Object { Write-Host "  [$($_.ProviderName) $($_.TimeCreated.ToString('HH:mm:ss'))] $((($_.Message -split "`n") | Select-Object -First 6) -join ' | ')" }
    Write-Host "  ---------------------" -ForegroundColor Yellow
}

# How long the panel takes to stop. Windows Installer only waits about 30 seconds for a service to stop.
function Measure-ServiceStop {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    Stop-Service $ServiceName -NoWait
    try { (Get-Service $ServiceName).WaitForStatus("Stopped", [TimeSpan]::FromSeconds(120)) } catch { Write-Diagnostics }
    $seconds = [Math]::Round($timer.Elapsed.TotalSeconds, 1)
    Start-Service $ServiceName
    $seconds
}

function Test-Rule([string]$Name) { [bool](Get-NetFirewallRule -DisplayName $Name -ErrorAction SilentlyContinue) }
function Get-Config { Get-Content (Join-Path $ConfigDir "appsettings.json") -Raw | ConvertFrom-Json }
function Get-ServiceStatus { (Get-Service $ServiceName -ErrorAction SilentlyContinue).Status }
# Runs an installer and returns its exit code. A run that hangs is stopped after $TimeoutMinutes and counted as a
# failure, so the job still finishes and uploads the installer logs.
function Invoke-Program([string]$File, [string[]]$Arguments, [int]$TimeoutMinutes = 10) {
    $msiLog = [array]::IndexOf($Arguments, "/l*v")
    $log = if ($msiLog -ge 0) { $Arguments[$msiLog + 1].Trim('"') } else { ($Arguments | Where-Object { $_ -like "/LOG=*" } | Select-Object -First 1) -replace '^/LOG=|"' }
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -PassThru
    $null = $process.Handle # keeps the exit code available after the process ends
    if ($process.WaitForExit($TimeoutMinutes * 60 * 1000)) {
        # Windows Installer occasionally fails on its own state (error 2203 reading inprogressinstallinfo.ipi, seen on
        # CI runners) or is busy with another installation (1618). Neither is about this package, so retry once.
        $transient = $process.ExitCode -eq 1618 -or ($log -and (Test-Path $log) -and (Select-String -Path $log -Pattern "Error 2203" -Quiet))
        if ($transient -and $process.ExitCode -ne 0 -and -not $script:retrying) {
            Write-Host "  [warn] $(Split-Path $File -Leaf) hit a Windows Installer error unrelated to the package (exit code $($process.ExitCode)); retrying in 30 seconds" -ForegroundColor Yellow
            Start-Sleep -Seconds 30
            $script:retrying = $true
            try { return Invoke-Program $File $Arguments $TimeoutMinutes } finally { $script:retrying = $false }
        }
        if ($process.ExitCode -notin 0, 3010) { Write-Diagnostics $log }
        return $process.ExitCode
    }

    Write-Host "  [FAIL] $(Split-Path $File -Leaf) $($Arguments -join ' ') didn't finish within $TimeoutMinutes minutes" -ForegroundColor Red
    $script:failures++
    Write-Diagnostics $log
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    return -1
}
function Test-RecoveryActions { [bool]((& sc.exe qfailure $ServiceName) -match "RESTART") }

# The setup wizard's uninstaller finishes in a copy of itself (_iu*.tmp) that outlives unins000.exe. Until it is done
# it is still deleting files, so installing again into the same folder straight away loses files to it.
function Wait-UninstallFinished {
    [bool](Wait-For {
        -not (Get-Process | Where-Object ProcessName -like "_iu*") -and -not (Get-ChildItem $InstallDir -Filter "unins*.dat" -ErrorAction SilentlyContinue)
    } 180)
}

# The setup wizard's uninstaller, as registered under its AppId (it isn't always unins000.exe). If it has gone
# missing, show what is in the folder and whether Windows Defender removed anything, rather than failing obscurely.
function Get-WizardUninstaller {
    $key = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{40CCD350-F3FF-47AD-A037-69959704C3A1}_is1"
    $registered = (Get-ItemProperty $key -ErrorAction SilentlyContinue).UninstallString
    $path = if ($registered) { $registered.Trim('"') } else { Join-Path $InstallDir "unins000.exe" }
    if (Test-Path $path) { return $path }
    Write-Host "  uninstaller $path is missing (registered: $([bool]$registered)); the folder contains:" -ForegroundColor Yellow
    Get-ChildItem $InstallDir -ErrorAction SilentlyContinue | Format-Table Name, Length, LastWriteTime | Out-String | Write-Host
    Get-WinEvent -LogName "Microsoft-Windows-Windows Defender/Operational" -MaxEvents 50 -ErrorAction SilentlyContinue |
        Where-Object Id -in 1116, 1117, 1118, 1119 |
        ForEach-Object { Write-Host "  [Defender $($_.TimeCreated.ToString('HH:mm:ss'))] $((($_.Message -split "`n") | Select-Object -First 8) -join ' | ')" }
    return $null
}

function Get-ServiceAccount { (Get-CimInstance Win32_Service -Filter "Name='$ServiceName'").StartName }

# Only SYSTEM, Administrators and the service's account have access, and nothing is inherited from the parent.
function Test-Private([string]$Path) {
    $acl = Get-Acl $Path
    $others = $acl.Access | Where-Object { $_.IdentityReference.Value -notin @("NT AUTHORITY\SYSTEM", "BUILTIN\Administrators", $ServiceAccount) }
    if ($others) { Write-Host "    $Path also grants: $(($others.IdentityReference.Value | Sort-Object -Unique) -join ', ')" }
    $acl.AreAccessRulesProtected -and -not $others
}

function Complete-Setup([int]$Port, [string]$ServerDirectory) {
    $form = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/setup" @web -SessionVariable session
    $token = [regex]::Match($form.Content, 'name="__RequestVerificationToken" value="([^"]+)"').Groups[1].Value
    $null = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/setup" -Method Post -WebSession $session @web -Body @{
        "_handler" = "setup"; "__RequestVerificationToken" = $token; "Input.Username" = "admin"
        "Input.Password" = "ci-only-password"; "Input.ConfirmPassword" = "ci-only-password"; "Input.ServerDirectory" = $ServerDirectory
    }
}

# Signing in needs the service to read settings.json and the sign-in keys, and to issue a cookie.
function Test-SignIn([int]$Port) {
    try {
        $form = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/login" @web -SessionVariable session
        $token = [regex]::Match($form.Content, 'name="__RequestVerificationToken" value="([^"]+)"').Groups[1].Value
        $page = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/login" -Method Post -WebSession $session @web -Body @{
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
    $uninstaller = Get-WizardUninstaller
    if ($uninstaller) { $null = Invoke-Program $uninstaller @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART") }
    Check ($uninstaller -and (Wait-UninstallFinished)) "previous release fully uninstalled"
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
$seconds = Measure-ServiceStop
Check ($seconds -lt 30) "service stops within 30 seconds after sign-ins (took $seconds s)"
Check ([bool](Wait-For { Get-Panel 8080 })) "panel answers again after the restart"

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
$uninstaller = Get-WizardUninstaller
$code = if ($uninstaller) { Invoke-Program $uninstaller @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/LOG=`"$LogDirectory\setup-uninstall.log`"") } else { -1 }
Check ($code -eq 0) "uninstaller exited with code 0 (got $code)"
Check (Wait-UninstallFinished) "uninstaller finished (nothing left running before the MSI installs into the same folder)"
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
    $seconds = Measure-ServiceStop
    Check ($seconds -lt 30) "service stops within 30 seconds (took $seconds s)"
    $null = Wait-For { Get-Panel 8090 }
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
if ($script:failures) { Write-Diagnostics; throw "$($script:failures) installer check(s) failed. Logs: $LogDirectory" }
Write-Host "All installer checks passed." -ForegroundColor Green
