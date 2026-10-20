<#
.SYNOPSIS
    Builds a self-contained, single-file release of Minecraft Server Manager.

.DESCRIPTION
    Produces dist\MinecraftServerManager-<runtime>\ (and a .zip of it) containing
    MinecraftServerManager.exe, wwwroot, appsettings.json and the service scripts.
    No .NET runtime is needed on the target machine.

.EXAMPLE
    .\scripts\publish.ps1
    .\scripts\publish.ps1 -Runtime win-arm64
#>
[CmdletBinding()]
param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root "src\MinecraftServerManager\MinecraftServerManager.csproj"
$output = Join-Path $root "dist\MinecraftServerManager-$Runtime"

if (Test-Path $output) { Remove-Item $output -Recurse -Force }

dotnet publish $project -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o $output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

Compress-Archive -Path (Join-Path $output "*") -DestinationPath "$output.zip" -Force
Write-Host "Published to $output" -ForegroundColor Green
Write-Host "Archive:     $output.zip"
