#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Verifies and installs dependencies for Memory Timeline Windows native application.

.DESCRIPTION
    This script checks for required dependencies for both development and production use,
    and automatically installs missing components on Windows 11.

.PARAMETER Mode
    Specify 'Development' for full development environment or 'Production' for runtime only.
    Default: Development

.PARAMETER SkipOptional
    Skip installation of optional components.

.EXAMPLE
    .\Setup-Dependencies.ps1 -Mode Development
    Sets up full development environment.

.EXAMPLE
    .\Setup-Dependencies.ps1 -Mode Production
    Sets up runtime dependencies only.

.NOTES
    Requires Administrator privileges.
    Tested on Windows 11 22H2 and later.
#>

param(
    [ValidateSet('Development', 'Production')]
    [string]$Mode = 'Development',

    [switch]$SkipOptional
)

# Script configuration
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'Continue'

# Minimum version requirements
$MinWindowsVersion = [Version]'10.0.22000.0' # Windows 11 21H2
$RecommendedWindowsVersion = [Version]'10.0.22621.0' # Windows 11 22H2
$MinDotNetVersion = [Version]'8.0.0'
$MinVSVersion = [Version]'17.8.0'
$MinWindowsSDKVersion = '10.0.22621.0'
$MinWindowsAppSDKVersion = [Version]'1.5.0'

# Color output helpers
function Write-Success {
    param([string]$Message)
    Write-Host "✓ $Message" -ForegroundColor Green
}

function Write-Info {
    param([string]$Message)
    Write-Host "ℹ $Message" -ForegroundColor Cyan
}

function Write-Warning {
    param([string]$Message)
    Write-Host "⚠ $Message" -ForegroundColor Yellow
}

function Write-Error {
    param([string]$Message)
    Write-Host "✗ $Message" -ForegroundColor Red
}

function Write-Header {
    param([string]$Message)
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host " $Message" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""
}

# Check if running as administrator
function Test-IsAdministrator {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Check Windows version
function Test-WindowsVersion {
    Write-Info "Checking Windows version..."

    $osVersion = [System.Environment]::OSVersion.Version

    if ($osVersion -lt $MinWindowsVersion) {
        Write-Error "Windows 11 21H2 or later is required. Current version: $osVersion"
        return $false
    }

    if ($osVersion -lt $RecommendedWindowsVersion) {
        Write-Warning "Windows 11 22H2 or later is recommended. Current version: $osVersion"
    } else {
        Write-Success "Windows version: $osVersion"
    }

    return $true
}

# Check .NET SDK
function Test-DotNetSDK {
    Write-Info "Checking .NET SDK..."

    try {
        $dotnetVersion = & dotnet --version 2>$null
        if ($dotnetVersion) {
            $version = [Version]$dotnetVersion
            if ($version -ge $MinDotNetVersion) {
                Write-Success ".NET SDK $dotnetVersion installed"
                return $true
            } else {
                Write-Warning ".NET SDK $dotnetVersion found, but version $MinDotNetVersion or later is required"
                return $false
            }
        }
    } catch {
        Write-Warning ".NET SDK not found"
        return $false
    }

    return $false
}

# Install .NET SDK
function Install-DotNetSDK {
    Write-Info "Installing .NET 8 SDK..."

    # Direct download link for .NET 8.0 SDK (x64)
    $installerUrl = "https://download.visualstudio.microsoft.com/download/pr/b280d97f-25a9-4ab7-a018-46a6c4142ca3/2e2cfa6e90682cf436b4d49e7e10d6e2/dotnet-sdk-8.0.101-win-x64.exe"
    $installerPath = "$env:TEMP\dotnet-sdk-8.0-installer.exe"

    try {
        Write-Info "Downloading .NET 8 SDK installer..."
        Invoke-WebRequest -Uri $installerUrl -OutFile $installerPath -UseBasicParsing

        Write-Info "Running installer..."
        Start-Process -FilePath $installerPath -ArgumentList "/quiet", "/norestart" -Wait

        Remove-Item $installerPath -Force

        # Refresh environment variables
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")

        Write-Success ".NET 8 SDK installed successfully"
        return $true
    } catch {
        Write-Error "Failed to install .NET SDK: $_"
        return $false
    }
}

# Check Visual Studio
function Test-VisualStudio {
    Write-Info "Checking Visual Studio 2022..."

    $vsWherePath = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"

    if (-not (Test-Path $vsWherePath)) {
        Write-Warning "Visual Studio 2022 not found"
        return $false
    }

    try {
        $vsInfo = & $vsWherePath -version "[17.8,18.0)" -latest -format json | ConvertFrom-Json

        if ($vsInfo) {
            $version = [Version]$vsInfo.catalog.productDisplayVersion
            Write-Success "Visual Studio $($vsInfo.displayName) version $version installed"

            # Check for required workloads
            $installedWorkloads = $vsInfo.properties.setupEngineFilePath
            Write-Info "Visual Studio installation found at: $($vsInfo.installationPath)"
            return $true
        }
    } catch {
        Write-Warning "Could not determine Visual Studio version"
        return $false
    }

    return $false
}

# Install Visual Studio (provide instructions)
function Install-VisualStudio {
    Write-Info "Visual Studio 2022 installation required for development."
    Write-Info ""
    Write-Info "Please download and install Visual Studio 2022 Community (or higher) from:"
    Write-Info "https://visualstudio.microsoft.com/downloads/"
    Write-Info ""
    Write-Info "Required workloads:"
    Write-Info "  • .NET desktop development"
    Write-Info "  • Universal Windows Platform development"
    Write-Info "  • Windows App SDK C# Templates"
    Write-Info ""

    $response = Read-Host "Open download page in browser? (Y/N)"
    if ($response -eq 'Y' -or $response -eq 'y') {
        Start-Process "https://visualstudio.microsoft.com/downloads/"
    }

    return $false
}

# Check Windows SDK
function Test-WindowsSDK {
    Write-Info "Checking Windows SDK..."

    $sdkPath = "HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots"

    if (Test-Path $sdkPath) {
        $sdkVersions = Get-ChildItem "HKLM:\SOFTWARE\Microsoft\Microsoft SDKs\Windows\v10.0\ProductVersions" -ErrorAction SilentlyContinue

        if ($sdkVersions) {
            $latestVersion = $sdkVersions | Select-Object -Last 1
            Write-Success "Windows SDK installed (version check in registry)"
            return $true
        }
    }

    # Alternative check
    $sdkInstallPath = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (Test-Path $sdkInstallPath) {
        $versions = Get-ChildItem $sdkInstallPath -Directory | Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' }
        if ($versions) {
            $latest = $versions | Sort-Object Name -Descending | Select-Object -First 1
            Write-Success "Windows SDK $($latest.Name) installed"
            return $true
        }
    }

    Write-Warning "Windows SDK not found"
    return $false
}

# Install Windows SDK
function Install-WindowsSDK {
    Write-Info "Installing Windows SDK..."

    $installerUrl = "https://go.microsoft.com/fwlink/?linkid=2272610" # Windows SDK for Windows 11
    $installerPath = "$env:TEMP\winsdksetup.exe"

    try {
        Write-Info "Downloading Windows SDK installer..."
        Invoke-WebRequest -Uri $installerUrl -OutFile $installerPath -UseBasicParsing

        Write-Info "Running installer (this may take several minutes)..."
        Start-Process -FilePath $installerPath -ArgumentList "/quiet", "/norestart", "/features", "+" -Wait

        Remove-Item $installerPath -Force
        Write-Success "Windows SDK installed successfully"
        return $true
    } catch {
        Write-Error "Failed to install Windows SDK: $_"
        Write-Info "Please download manually from: https://developer.microsoft.com/windows/downloads/windows-sdk/"
        return $false
    }
}

# Check Windows App SDK
function Test-WindowsAppSDK {
    Write-Info "Checking Windows App SDK..."

    # Check for WinAppSDK NuGet package in global packages folder
    $nugetPackagesPath = "$env:USERPROFILE\.nuget\packages\microsoft.windowsappsdk"

    if (Test-Path $nugetPackagesPath) {
        $versions = Get-ChildItem $nugetPackagesPath -Directory | Where-Object { $_.Name -match '^\d+\.\d+\.\d+' }
        if ($versions) {
            $latest = $versions | Sort-Object Name -Descending | Select-Object -First 1
            $version = [Version]$latest.Name

            if ($version -ge $MinWindowsAppSDKVersion) {
                Write-Success "Windows App SDK $($latest.Name) found in NuGet cache"
                return $true
            }
        }
    }

    # Check for Windows App SDK runtime
    $runtimePath = "${env:ProgramFiles}\WindowsApps"
    if (Test-Path $runtimePath) {
        $appSDKPackages = Get-ChildItem $runtimePath -Filter "Microsoft.WindowsAppRuntime*" -Directory -ErrorAction SilentlyContinue
        if ($appSDKPackages) {
            Write-Success "Windows App SDK runtime packages found"
            return $true
        }
    }

    Write-Warning "Windows App SDK not found or outdated"
    return $false
}

# Install Windows App SDK
function Install-WindowsAppSDK {
    Write-Info "Installing Windows App SDK..."

    # Install via winget
    try {
        Write-Info "Installing Windows App SDK via winget..."
        & winget install Microsoft.WindowsAppSDK.1.5 --accept-source-agreements --accept-package-agreements

        Write-Success "Windows App SDK installed successfully"
        return $true
    } catch {
        Write-Warning "Could not install via winget"
    }

    # Fallback: Install via NuGet (for development)
    if ($Mode -eq 'Development') {
        Write-Info "Windows App SDK will be installed via NuGet when building the project"
        return $true
    }

    return $false
}

# Check Git
function Test-Git {
    Write-Info "Checking Git..."

    try {
        $gitVersion = & git --version 2>$null
        if ($gitVersion) {
            Write-Success "$gitVersion installed"
            return $true
        }
    } catch {
        Write-Warning "Git not found"
        return $false
    }

    return $false
}

# Install Git
function Install-Git {
    Write-Info "Installing Git..."

    try {
        Write-Info "Installing Git via winget..."
        & winget install Git.Git --accept-source-agreements --accept-package-agreements

        # Refresh environment
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")

        Write-Success "Git installed successfully"
        return $true
    } catch {
        Write-Error "Failed to install Git: $_"
        Write-Info "Please download manually from: https://git-scm.com/download/win"
        return $false
    }
}

# Check PowerShell version
function Test-PowerShellVersion {
    Write-Info "Checking PowerShell version..."

    $psVersion = $PSVersionTable.PSVersion
    if ($psVersion.Major -ge 5) {
        Write-Success "PowerShell $psVersion installed"
        return $true
    } else {
        Write-Warning "PowerShell 5.0 or later recommended. Current: $psVersion"
        return $false
    }
}

# Check winget
function Test-Winget {
    Write-Info "Checking winget (Windows Package Manager)..."

    try {
        $wingetVersion = & winget --version 2>$null
        if ($wingetVersion) {
            Write-Success "winget $wingetVersion installed"
            return $true
        }
    } catch {
        Write-Warning "winget not found"
        return $false
    }

    return $false
}

# Install winget
function Install-Winget {
    Write-Info "Installing winget..."
    Write-Info "winget is included with Windows 11. If missing, please install App Installer from Microsoft Store."

    $response = Read-Host "Open Microsoft Store to install App Installer? (Y/N)"
    if ($response -eq 'Y' -or $response -eq 'y') {
        Start-Process "ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1"
    }

    return $false
}

# Check MSIX Packaging Tool (optional, for development)
function Test-MSIXPackagingTool {
    Write-Info "Checking MSIX Packaging Tool (optional)..."

    $msixTool = Get-AppxPackage -Name "Microsoft.MsixPackagingTool" -ErrorAction SilentlyContinue

    if ($msixTool) {
        Write-Success "MSIX Packaging Tool installed"
        return $true
    }

    Write-Warning "MSIX Packaging Tool not found (optional)"
    return $false
}

# Install MSIX Packaging Tool
function Install-MSIXPackagingTool {
    Write-Info "MSIX Packaging Tool can be installed from Microsoft Store (optional for development)"

    $response = Read-Host "Open Microsoft Store to install MSIX Packaging Tool? (Y/N)"
    if ($response -eq 'Y' -or $response -eq 'y') {
        Start-Process "ms-windows-store://pdp/?ProductId=9N5LW3JBCXKF"
    }

    return $true # Not critical
}

# Check Visual Studio Code (optional)
function Test-VSCode {
    Write-Info "Checking Visual Studio Code (optional)..."

    $vscodePath = "$env:LOCALAPPDATA\Programs\Microsoft VS Code\Code.exe"
    $vscodePath2 = "$env:ProgramFiles\Microsoft VS Code\Code.exe"

    if ((Test-Path $vscodePath) -or (Test-Path $vscodePath2)) {
        Write-Success "Visual Studio Code installed"
        return $true
    }

    try {
        $null = & code --version 2>$null
        Write-Success "Visual Studio Code installed"
        return $true
    } catch {
        Write-Warning "Visual Studio Code not found (optional)"
        return $false
    }
}

# Install VS Code
function Install-VSCode {
    Write-Info "Installing Visual Studio Code..."

    try {
        & winget install Microsoft.VisualStudioCode --accept-source-agreements --accept-package-agreements
        Write-Success "Visual Studio Code installed successfully"
        return $true
    } catch {
        Write-Warning "Failed to install VS Code via winget"
        return $false
    }
}

# Verify project dependencies
function Test-ProjectDependencies {
    Write-Info "Checking project dependencies..."

    $slnPath = Join-Path $PSScriptRoot "..\src\MemoryTimeline.sln"

    if (-not (Test-Path $slnPath)) {
        Write-Warning "Solution file not found at: $slnPath"
        return $false
    }

    try {
        Write-Info "Restoring NuGet packages..."
        Push-Location (Split-Path $slnPath)

        & dotnet restore

        if ($LASTEXITCODE -eq 0) {
            Write-Success "Project dependencies restored successfully"
            Pop-Location
            return $true
        } else {
            Write-Warning "Some dependencies could not be restored"
            Pop-Location
            return $false
        }
    } catch {
        Write-Error "Failed to restore dependencies: $_"
        Pop-Location
        return $false
    }
}

# Main execution
function Main {
    Write-Header "Memory Timeline - Dependency Setup"

    Write-Info "Mode: $Mode"
    Write-Info "Skip Optional: $SkipOptional"
    Write-Host ""

    # Check administrator
    if (-not (Test-IsAdministrator)) {
        Write-Error "This script requires Administrator privileges"
        Write-Info "Please run PowerShell as Administrator and try again"
        exit 1
    }

    $results = @{
        Required = @()
        Optional = @()
        Failed = @()
    }

    # Core requirements (both Development and Production)
    Write-Header "Core Requirements"

    # Windows version
    if (-not (Test-WindowsVersion)) {
        Write-Error "Windows 11 is required. Cannot continue."
        exit 1
    }
    $results.Required += "Windows 11"

    # PowerShell
    if (Test-PowerShellVersion) {
        $results.Required += "PowerShell"
    }

    # winget
    if (-not (Test-Winget)) {
        if (-not (Install-Winget)) {
            Write-Warning "winget installation required. Some automatic installations may fail."
        }
    } else {
        $results.Required += "winget"
    }

    # .NET SDK
    if (-not (Test-DotNetSDK)) {
        if (Install-DotNetSDK) {
            $results.Required += ".NET 8 SDK"
        } else {
            $results.Failed += ".NET 8 SDK"
        }
    } else {
        $results.Required += ".NET 8 SDK"
    }

    # Windows App SDK (Production runtime)
    if (-not (Test-WindowsAppSDK)) {
        if (Install-WindowsAppSDK) {
            $results.Required += "Windows App SDK"
        } else {
            $results.Failed += "Windows App SDK"
        }
    } else {
        $results.Required += "Windows App SDK"
    }

    # Development-specific requirements
    if ($Mode -eq 'Development') {
        Write-Header "Development Requirements"

        # Visual Studio
        if (-not (Test-VisualStudio)) {
            if (-not (Install-VisualStudio)) {
                Write-Warning "Visual Studio 2022 installation required for development"
                $results.Failed += "Visual Studio 2022"
            }
        } else {
            $results.Required += "Visual Studio 2022"
        }

        # Windows SDK
        if (-not (Test-WindowsSDK)) {
            if (Install-WindowsSDK) {
                $results.Required += "Windows SDK"
            } else {
                $results.Failed += "Windows SDK"
            }
        } else {
            $results.Required += "Windows SDK"
        }

        # Git
        if (-not (Test-Git)) {
            if (Install-Git) {
                $results.Required += "Git"
            } else {
                $results.Failed += "Git"
            }
        } else {
            $results.Required += "Git"
        }

        # Optional tools
        if (-not $SkipOptional) {
            Write-Header "Optional Development Tools"

            # MSIX Packaging Tool
            if (-not (Test-MSIXPackagingTool)) {
                if (Install-MSIXPackagingTool) {
                    $results.Optional += "MSIX Packaging Tool"
                }
            } else {
                $results.Optional += "MSIX Packaging Tool"
            }

            # VS Code
            if (-not (Test-VSCode)) {
                $response = Read-Host "Install Visual Studio Code? (Y/N)"
                if ($response -eq 'Y' -or $response -eq 'y') {
                    if (Install-VSCode) {
                        $results.Optional += "Visual Studio Code"
                    }
                }
            } else {
                $results.Optional += "Visual Studio Code"
            }
        }

        # Restore project dependencies
        Write-Header "Project Dependencies"
        if (Test-ProjectDependencies) {
            $results.Required += "Project Dependencies"
        } else {
            Write-Warning "Project dependencies could not be restored. You may need to run 'dotnet restore' manually."
        }
    }

    # Summary
    Write-Header "Setup Summary"

    Write-Host "Required components installed:" -ForegroundColor Green
    foreach ($item in $results.Required) {
        Write-Host "  ✓ $item" -ForegroundColor Green
    }
    Write-Host ""

    if ($results.Optional.Count -gt 0) {
        Write-Host "Optional components installed:" -ForegroundColor Cyan
        foreach ($item in $results.Optional) {
            Write-Host "  ✓ $item" -ForegroundColor Cyan
        }
        Write-Host ""
    }

    if ($results.Failed.Count -gt 0) {
        Write-Host "Components that failed to install:" -ForegroundColor Red
        foreach ($item in $results.Failed) {
            Write-Host "  ✗ $item" -ForegroundColor Red
        }
        Write-Host ""
        Write-Warning "Some components could not be installed automatically."
        Write-Warning "Please install them manually and re-run this script."
        exit 1
    }

    Write-Success "Setup completed successfully!"
    Write-Host ""

    if ($Mode -eq 'Development') {
        Write-Info "Next steps:"
        Write-Info "  1. Open Visual Studio 2022"
        Write-Info "  2. Open solution: windows-native\src\MemoryTimeline.sln"
        Write-Info "  3. Build and run the application"
        Write-Info ""
        Write-Info "Or use command line:"
        Write-Info "  cd windows-native\src"
        Write-Info "  dotnet build"
        Write-Info "  dotnet run --project MemoryTimeline"
    } else {
        Write-Info "Production runtime is ready."
        Write-Info "You can now run Memory Timeline MSIX packages on this system."
    }

    Write-Host ""
}

# Run main function
Main
