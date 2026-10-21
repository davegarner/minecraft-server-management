<#
.SYNOPSIS
    Builds a complete release: setup wizard (.exe), Windows Installer package (.msi), zip and checksums.

.DESCRIPTION
    Runs on Windows with the .NET 10 SDK and Inno Setup 6.3 or later (https://jrsoftware.org/isdl.php).
    The WiX Toolset is restored automatically from NuGet. Everything is written to artifacts\release:

      MinecraftServerManager-<version>-Setup.exe     setup wizard for most users
      MinecraftServerManager-<version>-x64.msi       for Group Policy / Intune / unattended installs
      MinecraftServerManager-<version>-win-x64.zip   portable files (install-service.ps1 included)
      SHA256SUMS.txt

.EXAMPLE
    .\scripts\build-release.ps1
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root "src\MinecraftServerManager\MinecraftServerManager.csproj"
if (-not $Version) {
    [xml]$xml = Get-Content $project
    $Version = @($xml.Project.PropertyGroup.Version | Where-Object { $_ })[0]
}

$artifacts = Join-Path $root "artifacts"
$publish = Join-Path $artifacts "publish"
$release = Join-Path $artifacts "release"
Remove-Item $publish, $release, (Join-Path $artifacts "wix") -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $release | Out-Null

function New-WixFileList([string]$PublishDir, [string]$Output) {
    # The service executable is authored in Package.wxs; the service scripts are for the zip only.
    $excluded = @("MinecraftServerManager.exe", "install-service.ps1", "uninstall-service.ps1")
    $sha1 = [System.Security.Cryptography.SHA1]::Create()
    $id = {
        param([string]$Prefix, [string]$Text)
        $hash = $sha1.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text.ToLowerInvariant()))
        $Prefix + "_" + ((($hash | ForEach-Object { $_.ToString("x2") }) -join "").Substring(0, 20))
    }
    $relative = { param([string]$Path) $Path.Substring($PublishDir.Length).TrimStart('\', '/') }
    $escape = { param([string]$Text) [Security.SecurityElement]::Escape($Text) }

    $xml = [Text.StringBuilder]::new()
    [void]$xml.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
    [void]$xml.AppendLine('  <Fragment>')
    [void]$xml.AppendLine('    <DirectoryRef Id="INSTALLFOLDER">')
    $writeDirectories = {
        param([string]$Path, [string]$Indent)
        foreach ($dir in Get-ChildItem $Path -Directory | Sort-Object Name) {
            $dirId = & $id "d" (& $relative $dir.FullName)
            [void]$xml.AppendLine("$Indent<Directory Id=`"$dirId`" Name=`"$(& $escape $dir.Name)`">")
            & $writeDirectories $dir.FullName "$Indent  "
            [void]$xml.AppendLine("$Indent</Directory>")
        }
    }
    & $writeDirectories $PublishDir "      "
    [void]$xml.AppendLine('    </DirectoryRef>')
    [void]$xml.AppendLine('    <ComponentGroup Id="PublishedFiles">')
    foreach ($file in Get-ChildItem $PublishDir -Recurse -File | Sort-Object FullName) {
        $rel = & $relative $file.FullName
        if ($excluded -contains $rel) { continue }
        $relDir = & $relative $file.DirectoryName
        $dirId = if ($relDir) { & $id "d" $relDir } else { "INSTALLFOLDER" }
        [void]$xml.AppendLine("      <Component Id=`"$(& $id 'c' $rel)`" Directory=`"$dirId`">")
        [void]$xml.AppendLine("        <File Id=`"$(& $id 'f' $rel)`" Source=`"`$(var.PublishDir)\$(& $escape $rel)`" KeyPath=`"yes`" />")
        [void]$xml.AppendLine("      </Component>")
    }
    [void]$xml.AppendLine('    </ComponentGroup>')
    [void]$xml.AppendLine('  </Fragment>')
    [void]$xml.AppendLine('</Wix>')

    New-Item -ItemType Directory -Force (Split-Path $Output) | Out-Null
    Set-Content -Path $Output -Value $xml.ToString() -Encoding utf8
}

Write-Host "== Publishing Minecraft Server Manager $Version" -ForegroundColor Cyan
dotnet publish $project -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:Version=$Version -o $publish
if ($LASTEXITCODE) { throw "dotnet publish failed." }

Write-Host "== Zip" -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $publish "*") -DestinationPath (Join-Path $release "MinecraftServerManager-$Version-win-x64.zip")

Write-Host "== Setup wizard (Inno Setup)" -ForegroundColor Cyan
$iscc = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 (ISCC.exe) was not found. Install it from https://jrsoftware.org/isdl.php" }
& $iscc /Q "/DAppVersion=$Version" "/DPublishDir=$publish" "/DOutputDir=$release" (Join-Path $root "installer\inno\MinecraftServerManager.iss")
if ($LASTEXITCODE) { throw "Inno Setup failed." }

Write-Host "== Windows Installer package (WiX)" -ForegroundColor Cyan
$filesWxs = Join-Path $artifacts "wix\Files.wxs"
New-WixFileList -PublishDir $publish -Output $filesWxs
dotnet build (Join-Path $root "installer\wix\MinecraftServerManager.Installer.wixproj") -c $Configuration `
    -p:Version=$Version -p:AppPublishDir=$publish -p:GeneratedFilesWxs=$filesWxs -o (Join-Path $artifacts "wix\out")
if ($LASTEXITCODE) { throw "WiX build failed." }
Copy-Item (Join-Path $artifacts "wix\out\MinecraftServerManager-$Version-x64.msi") $release

Write-Host "== Checksums" -ForegroundColor Cyan
Get-ChildItem $release -File | Where-Object { $_.Extension -in ".exe", ".msi", ".zip" } | Sort-Object Name | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
} | Set-Content (Join-Path $release "SHA256SUMS.txt") -Encoding ascii

Get-ChildItem $release | Format-Table Name, @{ Label = "Size (MB)"; Expression = { [math]::Round($_.Length / 1MB, 1) } } -AutoSize
