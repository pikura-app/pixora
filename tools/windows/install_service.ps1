#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs or uninstalls the Pixora Agent as a Windows Service.

.DESCRIPTION
    Registers Pixora.Agent.exe as a Windows Service named "PixoraAgent" using
    the built-in sc.exe tool (no third-party dependencies required).
    The service runs under the current user's account (LocalSystem is NOT used).

.PARAMETER Action
    install   - Register and start the service (default)
    uninstall - Stop and remove the service
    status    - Show current service status

.PARAMETER AgentExe
    Full path to Pixora.Agent.exe.
    Defaults to the sibling folder: <script_dir>\..\..\publish\win-x64\Pixora.Agent.exe

.EXAMPLE
    .\install_service.ps1 -Action install
    .\install_service.ps1 -Action install -AgentExe "C:\Program Files\Pixora\Pixora.Agent.exe"
    .\install_service.ps1 -Action uninstall
#>

param(
    [ValidateSet("install", "uninstall", "status")]
    [string]$Action = "install",

    [string]$AgentExe = ""
)

$ServiceName    = "PixoraAgent"
$ServiceDisplay = "Pixora Download Agent"
$ServiceDesc    = "Runs Pixora scheduled downloads in the background, even when the main application is closed."

# Resolve agent exe path
if ([string]::IsNullOrEmpty($AgentExe)) {
    $ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $AgentExe  = Join-Path $ScriptDir "..\..\publish\win-x64\Pixora.Agent.exe"
    $AgentExe  = [System.IO.Path]::GetFullPath($AgentExe)
}

function Get-ServiceStatus {
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc) {
        Write-Host "Service '$ServiceName' status: $($svc.Status)"
    } else {
        Write-Host "Service '$ServiceName' is not installed."
    }
}

switch ($Action) {

    "install" {
        if (-not (Test-Path $AgentExe)) {
            Write-Error "Pixora.Agent.exe not found at: $AgentExe"
            Write-Host "Build the agent first:  dotnet publish src\Pixora.Agent -c Release -r win-x64"
            exit 1
        }

        $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($existing) {
            Write-Host "Service already installed. Updating binary path..."
            & sc.exe config $ServiceName binPath= "`"$AgentExe`""
        } else {
            Write-Host "Installing '$ServiceName'..."
            & sc.exe create $ServiceName `
                binPath= "`"$AgentExe`"" `
                DisplayName= $ServiceDisplay `
                start= auto `
                obj= "NT AUTHORITY\NetworkService"

            & sc.exe description $ServiceName $ServiceDesc
            & sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000
        }

        Write-Host "Starting service..."
        Start-Service -Name $ServiceName
        Get-ServiceStatus
        Write-Host ""
        Write-Host "Done. The Pixora Agent will now start automatically with Windows."
    }

    "uninstall" {
        $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if (-not $existing) {
            Write-Host "Service '$ServiceName' is not installed."
            exit 0
        }

        Write-Host "Stopping '$ServiceName'..."
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue

        Write-Host "Removing '$ServiceName'..."
        & sc.exe delete $ServiceName
        Write-Host "Service removed."
    }

    "status" {
        Get-ServiceStatus
    }
}
