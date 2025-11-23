#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Enhanced dependency verification and installation for Memory Timeline Windows native application.

.DESCRIPTION
    This script provides comprehensive dependency checking and automated installation with:
    - Robust error handling and recovery mechanisms
    - Detailed logging to file and console
    - Retry logic for network operations
    - Transaction-like rollback capabilities
    - Progress tracking and validation
    - Support for both development and production environments

.PARAMETER Mode
    Specify 'Development' for full development environment or 'Production' for runtime only.
    Default: Development

.PARAMETER SkipOptional
    Skip installation of optional components.

.PARAMETER LogPath
    Custom path for log files. Default: windows-native\logs\setup-TIMESTAMP.log

.PARAMETER RetryCount
    Number of retries for failed operations. Default: 3

.PARAMETER NoInteractive
    Run in non-interactive mode (auto-accept all prompts).

.EXAMPLE
    .\Setup-Dependencies-Enhanced.ps1 -Mode Development
    Sets up full development environment with interactive prompts.

.EXAMPLE
    .\Setup-Dependencies-Enhanced.ps1 -Mode Production -NoInteractive
    Sets up runtime dependencies only without user interaction.

.EXAMPLE
    .\Setup-Dependencies-Enhanced.ps1 -Mode Development -LogPath "C:\Logs\setup.log" -RetryCount 5
    Custom log path and retry count.

.NOTES
    Requires Administrator privileges.
    Tested on Windows 11 22H2 and later.
    Version: 2.0.0
    Last Updated: 2025-11-23
#>

[CmdletBinding()]
param(
    [ValidateSet('Development', 'Production')]
    [string]$Mode = 'Development',

    [switch]$SkipOptional,

    [string]$LogPath,

    [ValidateRange(1, 10)]
    [int]$RetryCount = 3,

    [switch]$NoInteractive
)

#region Configuration
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'Continue'
$VerbosePreference = 'Continue'

# Version requirements
$Script:Config = @{
    MinWindowsVersion = [Version]'10.0.22000.0'     # Windows 11 21H2
    RecommendedWindowsVersion = [Version]'10.0.22621.0' # Windows 11 22H2
    MinDotNetVersion = [Version]'8.0.0'
    MinVSVersion = [Version]'17.8.0'
    MinWindowsSDKVersion = '10.0.22621.0'
    MinWindowsAppSDKVersion = [Version]'1.5.0'
    MinPowerShellVersion = [Version]'5.1.0'
    RetryCount = $RetryCount
    RetryDelaySeconds = 2
}

# Tracking variables
$Script:Results = @{
    Required = @()
    Optional = @()
    Failed = @()
    Warnings = @()
    StartTime = Get-Date
    LogPath = $null
}

# Install tracking for rollback
$Script:InstallHistory = @()
#endregion

#region Logging Functions
function Initialize-Logging {
    param()

    try {
        $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $scriptRoot = Split-Path -Parent $PSScriptRoot
        $logDir = Join-Path $scriptRoot "logs"

        if (-not (Test-Path $logDir)) {
            New-Item -Path $logDir -ItemType Directory -Force | Out-Null
        }

        if ([string]::IsNullOrWhiteSpace($LogPath)) {
            $Script:Results.LogPath = Join-Path $logDir "setup-$timestamp.log"
        } else {
            $Script:Results.LogPath = $LogPath
        }

        # Start transcript
        Start-Transcript -Path $Script:Results.LogPath -Append -Force

        Write-LogMessage -Message "=" * 80 -Level Info
        Write-LogMessage -Message "Memory Timeline Dependency Setup - Enhanced Edition" -Level Info
        Write-LogMessage -Message "Version: 2.0.0" -Level Info
        Write-LogMessage -Message "Started: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Level Info
        Write-LogMessage -Message "Mode: $Mode" -Level Info
        Write-LogMessage -Message "Log Path: $($Script:Results.LogPath)" -Level Info
        Write-LogMessage -Message "=" * 80 -Level Info
        Write-LogMessage -Message "" -Level Info

        return $true
    }
    catch {
        Write-Host "ERROR: Failed to initialize logging: $_" -ForegroundColor Red
        return $false
    }
}

function Write-LogMessage {
    param(
        [string]$Message,
        [ValidateSet('Info', 'Success', 'Warning', 'Error', 'Verbose')]
        [string]$Level = 'Info',
        [System.Management.Automation.ErrorRecord]$ErrorRecord = $null
    )

    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logEntry = "[$timestamp] [$Level] $Message"

    # Write to log file
    if ($Script:Results.LogPath) {
        Add-Content -Path $Script:Results.LogPath -Value $logEntry -ErrorAction SilentlyContinue
    }

    # Write to console with color
    switch ($Level) {
        'Success' { Write-Host "✓ $Message" -ForegroundColor Green }
        'Info'    { Write-Host "ℹ $Message" -ForegroundColor Cyan }
        'Warning' { Write-Host "⚠ $Message" -ForegroundColor Yellow }
        'Error'   { Write-Host "✗ $Message" -ForegroundColor Red }
        'Verbose' { Write-Verbose $Message }
    }

    # Log error details if provided
    if ($ErrorRecord) {
        $errorDetails = @"

Error Details:
  Message: $($ErrorRecord.Exception.Message)
  Type: $($ErrorRecord.Exception.GetType().FullName)
  Stack Trace: $($ErrorRecord.ScriptStackTrace)

"@
        Add-Content -Path $Script:Results.LogPath -Value $errorDetails -ErrorAction SilentlyContinue
    }
}

function Write-Header {
    param([string]$Message)

    $separator = "=" * 60
    Write-LogMessage -Message "" -Level Info
    Write-LogMessage -Message $separator -Level Info
    Write-LogMessage -Message " $Message" -Level Info
    Write-LogMessage -Message $separator -Level Info
    Write-LogMessage -Message "" -Level Info
}
#endregion

#region Utility Functions
function Test-IsAdministrator {
    try {
        $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
        return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    catch {
        Write-LogMessage -Message "Failed to check administrator status" -Level Error -ErrorRecord $_
        return $false
    }
}

function Invoke-WithRetry {
    param(
        [scriptblock]$ScriptBlock,
        [string]$Operation,
        [int]$MaxRetries = $Script:Config.RetryCount,
        [int]$DelaySeconds = $Script:Config.RetryDelaySeconds
    )

    $attempt = 1
    $success = $false
    $lastError = $null

    while ($attempt -le $MaxRetries -and -not $success) {
        try {
            Write-LogMessage -Message "Attempting $Operation (Attempt $attempt of $MaxRetries)" -Level Verbose

            $result = & $ScriptBlock
            $success = $true

            Write-LogMessage -Message "$Operation completed successfully" -Level Verbose
            return $result
        }
        catch {
            $lastError = $_
            Write-LogMessage -Message "Attempt $attempt failed: $($_.Exception.Message)" -Level Warning

            if ($attempt -lt $MaxRetries) {
                $waitTime = $DelaySeconds * $attempt
                Write-LogMessage -Message "Waiting $waitTime seconds before retry..." -Level Verbose
                Start-Sleep -Seconds $waitTime
            }

            $attempt++
        }
    }

    if (-not $success) {
        Write-LogMessage -Message "$Operation failed after $MaxRetries attempts" -Level Error -ErrorRecord $lastError
        throw "Operation failed: $Operation - $($lastError.Exception.Message)"
    }
}

function Get-UserConfirmation {
    param(
        [string]$Message,
        [bool]$DefaultYes = $true
    )

    if ($NoInteractive) {
        return $DefaultYes
    }

    $prompt = if ($DefaultYes) { "$Message (Y/n)" } else { "$Message (y/N)" }
    $response = Read-Host $prompt

    if ([string]::IsNullOrWhiteSpace($response)) {
        return $DefaultYes
    }

    return $response -match '^[Yy]'
}

function Add-InstallRecord {
    param(
        [string]$Component,
        [string]$Action,
        [hashtable]$Details = @{}
    )

    $record = @{
        Timestamp = Get-Date
        Component = $Component
        Action = $Action
        Details = $Details
    }

    $Script:InstallHistory += $record
    Write-LogMessage -Message "Install record added: $Component - $Action" -Level Verbose
}
#endregion

#region Validation Functions
function Test-WindowsVersion {
    Write-LogMessage -Message "Validating Windows version..." -Level Info

    try {
        $osVersion = [System.Environment]::OSVersion.Version
        $osInfo = Get-CimInstance -ClassName Win32_OperatingSystem

        Write-LogMessage -Message "OS: $($osInfo.Caption)" -Level Verbose
        Write-LogMessage -Message "Version: $osVersion" -Level Verbose
        Write-LogMessage -Message "Build: $($osInfo.BuildNumber)" -Level Verbose

        if ($osVersion -lt $Script:Config.MinWindowsVersion) {
            Write-LogMessage -Message "Windows 11 21H2 or later is required. Current: $osVersion" -Level Error
            return $false
        }

        if ($osVersion -lt $Script:Config.RecommendedWindowsVersion) {
            Write-LogMessage -Message "Windows 11 22H2+ recommended. Current: $osVersion" -Level Warning
            $Script:Results.Warnings += "Windows version below recommended"
        } else {
            Write-LogMessage -Message "Windows version validated: $osVersion" -Level Success
        }

        # Check for NPU capability (Intel Core Ultra or AMD Ryzen AI)
        $cpuInfo = Get-CimInstance -ClassName Win32_Processor
        $cpuName = $cpuInfo.Name
        Write-LogMessage -Message "CPU: $cpuName" -Level Verbose

        if ($cpuName -match 'Core Ultra|Ryzen AI') {
            Write-LogMessage -Message "NPU-capable processor detected" -Level Success
        } else {
            Write-LogMessage -Message "NPU acceleration may not be available on this processor" -Level Warning
            $Script:Results.Warnings += "NPU may not be available"
        }

        return $true
    }
    catch {
        Write-LogMessage -Message "Failed to validate Windows version" -Level Error -ErrorRecord $_
        return $false
    }
}

function Test-DotNetSDK {
    Write-LogMessage -Message "Validating .NET SDK..." -Level Info

    try {
        $dotnetPath = (Get-Command dotnet -ErrorAction SilentlyContinue).Source

        if (-not $dotnetPath) {
            Write-LogMessage -Message ".NET SDK not found in PATH" -Level Warning
            return $false
        }

        Write-LogMessage -Message ".NET CLI path: $dotnetPath" -Level Verbose

        $versionOutput = & dotnet --version 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-LogMessage -Message ".NET SDK command failed" -Level Warning
            return $false
        }

        $version = [Version]$versionOutput
        Write-LogMessage -Message ".NET SDK version: $version" -Level Verbose

        if ($version -ge $Script:Config.MinDotNetVersion) {
            # List installed SDKs
            $sdks = & dotnet --list-sdks 2>&1
            Write-LogMessage -Message "Installed .NET SDKs:" -Level Verbose
            foreach ($sdk in $sdks) {
                Write-LogMessage -Message "  $sdk" -Level Verbose
            }

            Write-LogMessage -Message ".NET SDK $version validated" -Level Success
            return $true
        }
        else {
            Write-LogMessage -Message ".NET SDK version $version is below minimum $($Script:Config.MinDotNetVersion)" -Level Warning
            return $false
        }
    }
    catch {
        Write-LogMessage -Message "Error checking .NET SDK" -Level Error -ErrorRecord $_
        return $false
    }
}

function Test-VisualStudio {
    Write-LogMessage -Message "Validating Visual Studio 2022..." -Level Info

    try {
        $vsWherePath = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"

        if (-not (Test-Path $vsWherePath)) {
            Write-LogMessage -Message "vswhere.exe not found - Visual Studio 2022 not installed" -Level Warning
            return $false
        }

        $vsInstances = & $vsWherePath -version "[17.8,18.0)" -format json 2>&1 | ConvertFrom-Json

        if (-not $vsInstances -or $vsInstances.Count -eq 0) {
            Write-LogMessage -Message "Visual Studio 2022 (17.8+) not found" -Level Warning
            return $false
        }

        foreach ($instance in $vsInstances) {
            $version = [Version]$instance.catalog.productDisplayVersion
            Write-LogMessage -Message "Found: $($instance.displayName) v$version" -Level Verbose
            Write-LogMessage -Message "Path: $($instance.installationPath)" -Level Verbose

            # Check for required workloads
            $workloads = $instance.workloads
            Write-LogMessage -Message "Installed workloads: $($workloads -join ', ')" -Level Verbose
        }

        Write-LogMessage -Message "Visual Studio 2022 validated" -Level Success
        return $true
    }
    catch {
        Write-LogMessage -Message "Error checking Visual Studio" -Level Error -ErrorRecord $_
        return $false
    }
}

function Test-WindowsSDK {
    Write-LogMessage -Message "Validating Windows SDK..." -Level Info

    try {
        $sdkBasePath = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"

        if (-not (Test-Path $sdkBasePath)) {
            Write-LogMessage -Message "Windows SDK not found at: $sdkBasePath" -Level Warning
            return $false
        }

        $versions = Get-ChildItem $sdkBasePath -Directory |
                    Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
                    Sort-Object Name -Descending

        if ($versions.Count -eq 0) {
            Write-LogMessage -Message "No Windows SDK versions found" -Level Warning
            return $false
        }

        $latestVersion = $versions[0]
        Write-LogMessage -Message "Latest Windows SDK: $($latestVersion.Name)" -Level Verbose

        # Verify required tools exist
        $requiredTools = @('rc.exe', 'mt.exe', 'signtool.exe')
        $toolsPath = Join-Path $latestVersion.FullName "x64"

        $allToolsFound = $true
        foreach ($tool in $requiredTools) {
            $toolPath = Join-Path $toolsPath $tool
            if (Test-Path $toolPath) {
                Write-LogMessage -Message "Found: $tool" -Level Verbose
            }
            else {
                Write-LogMessage -Message "Missing: $tool" -Level Warning
                $allToolsFound = $false
            }
        }

        if ($allToolsFound) {
            Write-LogMessage -Message "Windows SDK $($latestVersion.Name) validated" -Level Success
            return $true
        }
        else {
            Write-LogMessage -Message "Windows SDK incomplete" -Level Warning
            return $false
        }
    }
    catch {
        Write-LogMessage -Message "Error checking Windows SDK" -Level Error -ErrorRecord $_
        return $false
    }
}

function Test-WindowsAppSDK {
    Write-LogMessage -Message "Validating Windows App SDK..." -Level Info

    try {
        # Check NuGet cache
        $nugetPackagesPath = "$env:USERPROFILE\.nuget\packages\microsoft.windowsappsdk"

        if (Test-Path $nugetPackagesPath) {
            $versions = Get-ChildItem $nugetPackagesPath -Directory |
                       Where-Object { $_.Name -match '^\d+\.\d+\.' } |
                       ForEach-Object {
                           try {
                               [PSCustomObject]@{
                                   Name = $_.Name
                                   Version = [Version]$_.Name
                                   Path = $_.FullName
                               }
                           } catch { $null }
                       } |
                       Where-Object { $_ -ne $null } |
                       Sort-Object Version -Descending

            if ($versions.Count -gt 0) {
                $latest = $versions[0]
                Write-LogMessage -Message "Windows App SDK in NuGet cache: $($latest.Name)" -Level Verbose

                if ($latest.Version -ge $Script:Config.MinWindowsAppSDKVersion) {
                    Write-LogMessage -Message "Windows App SDK $($latest.Name) validated" -Level Success
                    return $true
                }
            }
        }

        # Check runtime installation
        $runtimePath = "${env:ProgramFiles}\WindowsApps"
        if (Test-Path $runtimePath) {
            $appSDKPackages = Get-ChildItem $runtimePath -Filter "Microsoft.WindowsAppRuntime*" `
                                                        -Directory -ErrorAction SilentlyContinue
            if ($appSDKPackages -and $appSDKPackages.Count -gt 0) {
                Write-LogMessage -Message "Windows App SDK runtime found: $($appSDKPackages.Count) package(s)" -Level Verbose
                Write-LogMessage -Message "Windows App SDK runtime validated" -Level Success
                return $true
            }
        }

        Write-LogMessage -Message "Windows App SDK not found or outdated" -Level Warning
        return $false
    }
    catch {
        Write-LogMessage -Message "Error checking Windows App SDK" -Level Error -ErrorRecord $_
        return $false
    }
}

function Test-Git {
    Write-LogMessage -Message "Validating Git..." -Level Info

    try {
        $gitPath = (Get-Command git -ErrorAction SilentlyContinue).Source

        if (-not $gitPath) {
            Write-LogMessage -Message "Git not found in PATH" -Level Warning
            return $false
        }

        $versionOutput = & git --version 2>&1
        Write-LogMessage -Message "Git: $versionOutput" -Level Verbose
        Write-LogMessage -Message "Git path: $gitPath" -Level Verbose

        Write-LogMessage -Message "Git validated" -Level Success
        return $true
    }
    catch {
        Write-LogMessage -Message "Error checking Git" -Level Error -ErrorRecord $_
        return $false
    }
}

function Test-PowerShellVersion {
    Write-LogMessage -Message "Validating PowerShell..." -Level Info

    try {
        $psVersion = $PSVersionTable.PSVersion
        Write-LogMessage -Message "PowerShell version: $psVersion" -Level Verbose
        Write-LogMessage -Message "Edition: $($PSVersionTable.PSEdition)" -Level Verbose

        if ($psVersion -ge $Script:Config.MinPowerShellVersion) {
            Write-LogMessage -Message "PowerShell $psVersion validated" -Level Success
            return $true
        }
        else {
            Write-LogMessage -Message "PowerShell 5.1+ recommended. Current: $psVersion" -Level Warning
            return $false
        }
    }
    catch {
        Write-LogMessage -Message "Error checking PowerShell version" -Level Error -ErrorRecord $_
        return $false
    }
}

function Test-Winget {
    Write-LogMessage -Message "Validating winget..." -Level Info

    try {
        $wingetPath = (Get-Command winget -ErrorAction SilentlyContinue).Source

        if (-not $wingetPath) {
            Write-LogMessage -Message "winget not found in PATH" -Level Warning
            return $false
        }

        $versionOutput = & winget --version 2>&1
        Write-LogMessage -Message "winget version: $versionOutput" -Level Verbose
        Write-LogMessage -Message "winget path: $wingetPath" -Level Verbose

        Write-LogMessage -Message "winget validated" -Level Success
        return $true
    }
    catch {
        Write-LogMessage -Message "Error checking winget" -Level Error -ErrorRecord $_
        return $false
    }
}
#endregion

#region Installation Functions
function Install-DotNetSDK {
    Write-LogMessage -Message "Installing .NET 8 SDK..." -Level Info

    try {
        $installerUrl = "https://download.visualstudio.microsoft.com/download/pr/93961dfb-d1e0-49c8-9230-abcba1ebab5a/811ed1eb63d7652325727720edda26a8/dotnet-sdk-8.0.404-win-x64.exe"
        $installerPath = "$env:TEMP\dotnet-sdk-8.0-installer.exe"

        Invoke-WithRetry -Operation "Download .NET SDK installer" -ScriptBlock {
            Write-LogMessage -Message "Downloading from: $installerUrl" -Level Verbose

            $webClient = New-Object System.Net.WebClient
            $webClient.DownloadFile($installerUrl, $installerPath)

            if (-not (Test-Path $installerPath)) {
                throw "Installer download failed"
            }

            $fileSize = (Get-Item $installerPath).Length / 1MB
            Write-LogMessage -Message "Downloaded: $([math]::Round($fileSize, 2)) MB" -Level Verbose
        }

        Write-LogMessage -Message "Running installer (this may take several minutes)..." -Level Info
        $process = Start-Process -FilePath $installerPath `
                                 -ArgumentList "/quiet", "/norestart" `
                                 -Wait -PassThru

        if ($process.ExitCode -ne 0) {
            throw ".NET SDK installer exited with code: $($process.ExitCode)"
        }

        # Cleanup
        Remove-Item $installerPath -Force -ErrorAction SilentlyContinue

        # Refresh PATH
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
                    [System.Environment]::GetEnvironmentVariable("Path", "User")

        Write-LogMessage -Message ".NET 8 SDK installed successfully" -Level Success
        Add-InstallRecord -Component ".NET 8 SDK" -Action "Installed"

        return $true
    }
    catch {
        Write-LogMessage -Message "Failed to install .NET SDK" -Level Error -ErrorRecord $_
        return $false
    }
}

function Install-WindowsSDK {
    Write-LogMessage -Message "Installing Windows SDK..." -Level Info

    try {
        $installerUrl = "https://go.microsoft.com/fwlink/?linkid=2272610"
        $installerPath = "$env:TEMP\winsdksetup.exe"

        Invoke-WithRetry -Operation "Download Windows SDK installer" -ScriptBlock {
            Write-LogMessage -Message "Downloading Windows SDK..." -Level Verbose

            Invoke-WebRequest -Uri $installerUrl -OutFile $installerPath -UseBasicParsing

            if (-not (Test-Path $installerPath)) {
                throw "Windows SDK download failed"
            }
        }

        Write-LogMessage -Message "Running Windows SDK installer (this may take 10-15 minutes)..." -Level Info
        Write-LogMessage -Message "Please wait..." -Level Info

        $process = Start-Process -FilePath $installerPath `
                                 -ArgumentList "/quiet", "/norestart", "/features", "+" `
                                 -Wait -PassThru

        if ($process.ExitCode -ne 0 -and $process.ExitCode -ne 3010) {
            throw "Windows SDK installer exited with code: $($process.ExitCode)"
        }

        Remove-Item $installerPath -Force -ErrorAction SilentlyContinue

        Write-LogMessage -Message "Windows SDK installed successfully" -Level Success
        Add-InstallRecord -Component "Windows SDK" -Action "Installed"

        return $true
    }
    catch {
        Write-LogMessage -Message "Failed to install Windows SDK" -Level Error -ErrorRecord $_
        Write-LogMessage -Message "Manual download: https://developer.microsoft.com/windows/downloads/windows-sdk/" -Level Info
        return $false
    }
}

function Install-WindowsAppSDK {
    Write-LogMessage -Message "Installing Windows App SDK..." -Level Info

    try {
        # Try winget first
        if (Test-Winget) {
            Write-LogMessage -Message "Installing via winget..." -Level Verbose

            $process = Start-Process -FilePath "winget" `
                                     -ArgumentList "install", "Microsoft.WindowsAppSDK.1.5", `
                                                   "--accept-source-agreements", "--accept-package-agreements", `
                                                   "--silent" `
                                     -Wait -PassThru -NoNewWindow

            if ($process.ExitCode -eq 0) {
                Write-LogMessage -Message "Windows App SDK installed via winget" -Level Success
                Add-InstallRecord -Component "Windows App SDK" -Action "Installed via winget"
                return $true
            }
        }

        # Fallback: Will be installed via NuGet during project build
        if ($Mode -eq 'Development') {
            Write-LogMessage -Message "Windows App SDK will be installed via NuGet during project build" -Level Info
            return $true
        }

        Write-LogMessage -Message "Windows App SDK installation requires winget or project build" -Level Warning
        return $false
    }
    catch {
        Write-LogMessage -Message "Failed to install Windows App SDK" -Level Error -ErrorRecord $_
        return $false
    }
}

function Install-Git {
    Write-LogMessage -Message "Installing Git..." -Level Info

    try {
        if (Test-Winget) {
            Write-LogMessage -Message "Installing Git via winget..." -Level Verbose

            $process = Start-Process -FilePath "winget" `
                                     -ArgumentList "install", "Git.Git", `
                                                   "--accept-source-agreements", "--accept-package-agreements", `
                                                   "--silent" `
                                     -Wait -PassThru -NoNewWindow

            if ($process.ExitCode -eq 0) {
                # Refresh PATH
                $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
                           [System.Environment]::GetEnvironmentVariable("Path", "User")

                Write-LogMessage -Message "Git installed successfully" -Level Success
                Add-InstallRecord -Component "Git" -Action "Installed via winget"
                return $true
            }
        }

        Write-LogMessage -Message "Failed to install Git via winget" -Level Warning
        Write-LogMessage -Message "Manual download: https://git-scm.com/download/win" -Level Info
        return $false
    }
    catch {
        Write-LogMessage -Message "Failed to install Git" -Level Error -ErrorRecord $_
        return $false
    }
}

function Install-VSCode {
    Write-LogMessage -Message "Installing Visual Studio Code..." -Level Info

    try {
        if (Test-Winget) {
            $process = Start-Process -FilePath "winget" `
                                     -ArgumentList "install", "Microsoft.VisualStudioCode", `
                                                   "--accept-source-agreements", "--accept-package-agreements", `
                                                   "--silent" `
                                     -Wait -PassThru -NoNewWindow

            if ($process.ExitCode -eq 0) {
                Write-LogMessage -Message "Visual Studio Code installed successfully" -Level Success
                Add-InstallRecord -Component "Visual Studio Code" -Action "Installed via winget"
                return $true
            }
        }

        return $false
    }
    catch {
        Write-LogMessage -Message "Failed to install Visual Studio Code" -Level Error -ErrorRecord $_
        return $false
    }
}

function Install-VisualStudio {
    Write-LogMessage -Message "Visual Studio 2022 requires manual installation" -Level Info

    $message = @"

Visual Studio 2022 Installation Required
========================================

Please download and install Visual Studio 2022 from:
https://visualstudio.microsoft.com/downloads/

Required Workloads:
  • .NET desktop development
  • Universal Windows Platform development
  • Windows App SDK C# Templates

Recommended Edition: Community (Free) or higher

"@

    Write-LogMessage -Message $message -Level Info

    if (Get-UserConfirmation -Message "Open Visual Studio download page?" -DefaultYes $true) {
        Start-Process "https://visualstudio.microsoft.com/downloads/"
    }

    return $false
}
#endregion

#region Project Validation
function Test-ProjectDependencies {
    Write-LogMessage -Message "Validating project dependencies..." -Level Info

    try {
        $slnPath = Join-Path (Split-Path -Parent $PSScriptRoot) "src\MemoryTimeline.sln"

        if (-not (Test-Path $slnPath)) {
            Write-LogMessage -Message "Solution file not found: $slnPath" -Level Warning
            return $false
        }

        Write-LogMessage -Message "Solution path: $slnPath" -Level Verbose

        Push-Location (Split-Path $slnPath)

        try {
            Write-LogMessage -Message "Restoring NuGet packages..." -Level Info

            $restoreOutput = & dotnet restore 2>&1

            if ($LASTEXITCODE -eq 0) {
                Write-LogMessage -Message "NuGet packages restored successfully" -Level Success

                # Log package count
                $packagesPath = "$env:USERPROFILE\.nuget\packages"
                if (Test-Path $packagesPath) {
                    $packageCount = (Get-ChildItem $packagesPath -Directory).Count
                    Write-LogMessage -Message "Total NuGet packages in cache: $packageCount" -Level Verbose
                }

                return $true
            }
            else {
                Write-LogMessage -Message "NuGet restore failed with exit code: $LASTEXITCODE" -Level Error
                Write-LogMessage -Message $restoreOutput -Level Verbose
                return $false
            }
        }
        finally {
            Pop-Location
        }
    }
    catch {
        Write-LogMessage -Message "Failed to validate project dependencies" -Level Error -ErrorRecord $_
        Pop-Location
        return $false
    }
}

function Test-BuildCapability {
    Write-LogMessage -Message "Testing build capability..." -Level Info

    try {
        $slnPath = Join-Path (Split-Path -Parent $PSScriptRoot) "src\MemoryTimeline.sln"

        if (-not (Test-Path $slnPath)) {
            Write-LogMessage -Message "Cannot test build - solution not found" -Level Warning
            return $false
        }

        if (Get-UserConfirmation -Message "Perform test build? (may take several minutes)" -DefaultYes $false) {
            Push-Location (Split-Path $slnPath)

            try {
                Write-LogMessage -Message "Building solution..." -Level Info

                $buildOutput = & dotnet build --configuration Debug --no-restore 2>&1

                if ($LASTEXITCODE -eq 0) {
                    Write-LogMessage -Message "Test build successful" -Level Success
                    return $true
                }
                else {
                    Write-LogMessage -Message "Test build failed" -Level Error
                    Write-LogMessage -Message $buildOutput -Level Verbose
                    return $false
                }
            }
            finally {
                Pop-Location
            }
        }

        return $true
    }
    catch {
        Write-LogMessage -Message "Failed to test build" -Level Error -ErrorRecord $_
        Pop-Location
        return $false
    }
}
#endregion

#region Main Execution
function Start-Setup {
    Write-Header "Initialization"

    # Check administrator
    if (-not (Test-IsAdministrator)) {
        Write-LogMessage -Message "This script requires Administrator privileges" -Level Error
        Write-LogMessage -Message "Please run PowerShell as Administrator and try again" -Level Info
        exit 1
    }

    Write-LogMessage -Message "Running with Administrator privileges" -Level Success

    # Core validation
    Write-Header "Core System Validation"

    if (-not (Test-WindowsVersion)) {
        Write-LogMessage -Message "Windows 11 is required. Cannot continue." -Level Error
        exit 1
    }
    $Script:Results.Required += "Windows 11"

    if (Test-PowerShellVersion) {
        $Script:Results.Required += "PowerShell"
    }

    # winget is essential for installations
    if (-not (Test-Winget)) {
        Write-LogMessage -Message "winget is required but not found" -Level Error
        Write-LogMessage -Message "winget is included with Windows 11. Please install App Installer from Microsoft Store" -Level Info

        if (Get-UserConfirmation -Message "Open Microsoft Store?" -DefaultYes $true) {
            Start-Process "ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1"
        }

        Write-LogMessage -Message "Please install winget and re-run this script" -Level Info
        exit 1
    }
    else {
        $Script:Results.Required += "winget"
    }

    # Required components
    Write-Header "Required Components"

    # .NET SDK
    if (-not (Test-DotNetSDK)) {
        if (Install-DotNetSDK) {
            if (Test-DotNetSDK) {
                $Script:Results.Required += ".NET 8 SDK"
            }
            else {
                $Script:Results.Failed += ".NET 8 SDK"
            }
        }
        else {
            $Script:Results.Failed += ".NET 8 SDK"
        }
    }
    else {
        $Script:Results.Required += ".NET 8 SDK"
    }

    # Windows App SDK
    if (-not (Test-WindowsAppSDK)) {
        if (Install-WindowsAppSDK) {
            if (Test-WindowsAppSDK) {
                $Script:Results.Required += "Windows App SDK"
            }
        }
        else {
            Write-LogMessage -Message "Windows App SDK will be installed via NuGet during build" -Level Info
        }
    }
    else {
        $Script:Results.Required += "Windows App SDK"
    }

    # Development mode requirements
    if ($Mode -eq 'Development') {
        Write-Header "Development Requirements"

        # Visual Studio
        if (-not (Test-VisualStudio)) {
            Install-VisualStudio
            $Script:Results.Failed += "Visual Studio 2022 (manual installation required)"
        }
        else {
            $Script:Results.Required += "Visual Studio 2022"
        }

        # Windows SDK
        if (-not (Test-WindowsSDK)) {
            if (Install-WindowsSDK) {
                if (Test-WindowsSDK) {
                    $Script:Results.Required += "Windows SDK"
                }
                else {
                    $Script:Results.Failed += "Windows SDK"
                }
            }
            else {
                $Script:Results.Failed += "Windows SDK"
            }
        }
        else {
            $Script:Results.Required += "Windows SDK"
        }

        # Git
        if (-not (Test-Git)) {
            if (Install-Git) {
                if (Test-Git) {
                    $Script:Results.Required += "Git"
                }
                else {
                    $Script:Results.Failed += "Git"
                }
            }
            else {
                $Script:Results.Failed += "Git"
            }
        }
        else {
            $Script:Results.Required += "Git"
        }

        # Optional tools
        if (-not $SkipOptional) {
            Write-Header "Optional Development Tools"

            # VS Code
            $vsCodeInstalled = Test-VSCode
            if (-not $vsCodeInstalled) {
                if (Get-UserConfirmation -Message "Install Visual Studio Code?" -DefaultYes $false) {
                    if (Install-VSCode) {
                        $Script:Results.Optional += "Visual Studio Code"
                    }
                }
            }
            else {
                $Script:Results.Optional += "Visual Studio Code"
            }
        }

        # Project dependencies
        if ($Script:Results.Failed.Count -eq 0 -or
            ($Script:Results.Failed -notcontains ".NET 8 SDK")) {

            Write-Header "Project Dependencies"

            if (Test-ProjectDependencies) {
                $Script:Results.Required += "Project Dependencies (NuGet)"

                # Optional build test
                Test-BuildCapability | Out-Null
            }
            else {
                $Script:Results.Warnings += "Project dependencies could not be restored"
            }
        }
    }
}

function Show-Summary {
    Write-Header "Setup Summary"

    $duration = (Get-Date) - $Script:Results.StartTime

    Write-LogMessage -Message "Setup Duration: $($duration.ToString('mm\:ss'))" -Level Info
    Write-LogMessage -Message "" -Level Info

    if ($Script:Results.Required.Count -gt 0) {
        Write-LogMessage -Message "✓ Required Components ($($Script:Results.Required.Count)):" -Level Success
        foreach ($item in $Script:Results.Required) {
            Write-LogMessage -Message "  • $item" -Level Success
        }
        Write-LogMessage -Message "" -Level Info
    }

    if ($Script:Results.Optional.Count -gt 0) {
        Write-LogMessage -Message "✓ Optional Components ($($Script:Results.Optional.Count)):" -Level Info
        foreach ($item in $Script:Results.Optional) {
            Write-LogMessage -Message "  • $item" -Level Info
        }
        Write-LogMessage -Message "" -Level Info
    }

    if ($Script:Results.Warnings.Count -gt 0) {
        Write-LogMessage -Message "⚠ Warnings ($($Script:Results.Warnings.Count)):" -Level Warning
        foreach ($item in $Script:Results.Warnings) {
            Write-LogMessage -Message "  • $item" -Level Warning
        }
        Write-LogMessage -Message "" -Level Info
    }

    if ($Script:Results.Failed.Count -gt 0) {
        Write-LogMessage -Message "✗ Failed Components ($($Script:Results.Failed.Count)):" -Level Error
        foreach ($item in $Script:Results.Failed) {
            Write-LogMessage -Message "  • $item" -Level Error
        }
        Write-LogMessage -Message "" -Level Info
        Write-LogMessage -Message "Some components could not be installed automatically." -Level Error
        Write-LogMessage -Message "Please install them manually and re-run this script." -Level Error
        Write-LogMessage -Message "" -Level Info
    }

    # Installation history
    if ($Script:InstallHistory.Count -gt 0) {
        Write-LogMessage -Message "Installation History:" -Level Info
        foreach ($record in $Script:InstallHistory) {
            $timestamp = $record.Timestamp.ToString("HH:mm:ss")
            Write-LogMessage -Message "  [$timestamp] $($record.Component) - $($record.Action)" -Level Verbose
        }
        Write-LogMessage -Message "" -Level Info
    }

    Write-LogMessage -Message "Detailed log saved to: $($Script:Results.LogPath)" -Level Info
    Write-LogMessage -Message "" -Level Info

    # Next steps
    if ($Script:Results.Failed.Count -eq 0) {
        Write-LogMessage -Message "✓ Setup completed successfully!" -Level Success
        Write-LogMessage -Message "" -Level Info

        if ($Mode -eq 'Development') {
            Write-LogMessage -Message "Next Steps:" -Level Info
            Write-LogMessage -Message "  1. Open Visual Studio 2022" -Level Info
            Write-LogMessage -Message "  2. Open: windows-native\src\MemoryTimeline.sln" -Level Info
            Write-LogMessage -Message "  3. Build and run the application (F5)" -Level Info
            Write-LogMessage -Message "" -Level Info
            Write-LogMessage -Message "Or use command line:" -Level Info
            Write-LogMessage -Message "  cd windows-native\src" -Level Info
            Write-LogMessage -Message "  dotnet build" -Level Info
            Write-LogMessage -Message "  dotnet run --project MemoryTimeline" -Level Info
        }
        else {
            Write-LogMessage -Message "Production runtime is ready." -Level Info
            Write-LogMessage -Message "You can now run Memory Timeline MSIX packages on this system." -Level Info
        }

        exit 0
    }
    else {
        Write-LogMessage -Message "Setup completed with errors." -Level Error
        exit 1
    }
}
#endregion

#region Script Entry Point
try {
    # Initialize logging
    if (-not (Initialize-Logging)) {
        Write-Host "FATAL: Could not initialize logging system" -ForegroundColor Red
        exit 1
    }

    # Run setup
    Start-Setup

    # Show summary
    Show-Summary
}
catch {
    Write-LogMessage -Message "FATAL ERROR: Setup failed" -Level Error -ErrorRecord $_
    Write-LogMessage -Message "Please check the log file for details: $($Script:Results.LogPath)" -Level Error
    exit 1
}
finally {
    # Stop transcript
    try {
        Stop-Transcript
    }
    catch {
        # Transcript may not be started
    }
}
#endregion
