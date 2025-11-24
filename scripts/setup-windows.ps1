<#
.SYNOPSIS
    Verify and install dependencies for Memory Timeline Application on Windows 11

.DESCRIPTION
    This script checks for and installs all required dependencies for both
    development and production environments on Windows 11. It supports
    automatic installation via winget or manual installation prompts.

.PARAMETER Mode
    Specify 'dev' for development dependencies or 'prod' for production only.
    Default is 'dev'.

.PARAMETER AutoInstall
    If specified, automatically installs missing dependencies without prompting.

.EXAMPLE
    .\setup-windows.ps1 -Mode dev -AutoInstall
    .\setup-windows.ps1 -Mode prod

.NOTES
    Requires Administrator privileges for installing system dependencies.
#>

param(
    [Parameter()]
    [ValidateSet('dev', 'prod')]
    [string]$Mode = 'dev',

    [Parameter()]
    [switch]$AutoInstall
)

# ============================================
# Logging Setup
# ============================================
# Create logs directory if it doesn't exist
$logDir = Join-Path $PSScriptRoot "logs"
if (-not (Test-Path $logDir)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}

# Create timestamped log file
$timestamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
$logFile = Join-Path $logDir "setup-windows_$timestamp.log"

# Initialize log file with UTF-8 encoding
$null = New-Item -ItemType File -Path $logFile -Force

# Function to write to both console and log file
function Write-Log {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Message,

        [Parameter()]
        [ValidateSet('Info', 'Success', 'Warning', 'Error', 'Debug')]
        [string]$Level = 'Info',

        [Parameter()]
        [switch]$NoConsole
    )

    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logEntry = "[$timestamp] [$Level] $Message"

    # Write to log file
    Add-Content -Path $script:logFile -Value $logEntry -Encoding UTF8

    # Write to console unless suppressed
    if (-not $NoConsole) {
        switch ($Level) {
            'Success' { Write-Host $Message -ForegroundColor Green }
            'Warning' { Write-Host $Message -ForegroundColor Yellow }
            'Error'   { Write-Host $Message -ForegroundColor Red }
            'Info'    { Write-Host $Message -ForegroundColor Cyan }
            'Debug'   { Write-Host $Message -ForegroundColor Gray }
        }
    }
}

# Function to log exceptions with full details
function Write-ErrorLog {
    param(
        [Parameter(Mandatory=$true)]
        [System.Management.Automation.ErrorRecord]$ErrorRecord,

        [Parameter()]
        [string]$Context
    )

    $errorDetails = @"

========================================
ERROR DETAILS
========================================
Context: $Context
Message: $($ErrorRecord.Exception.Message)
Type: $($ErrorRecord.Exception.GetType().FullName)
Stack Trace:
$($ErrorRecord.ScriptStackTrace)
Target Object: $($ErrorRecord.TargetObject)
Category: $($ErrorRecord.CategoryInfo.Category)
Fully Qualified Error ID: $($ErrorRecord.FullyQualifiedErrorId)
========================================
"@

    Write-Log -Message $errorDetails -Level Error -NoConsole
    Write-Log -Message "✗ Error: $($ErrorRecord.Exception.Message)" -Level Error
}

# Log system information
Write-Log -Message "========================================" -Level Info
Write-Log -Message "Memory Timeline - Dependency Setup" -Level Info
Write-Log -Message "Windows 11 Setup Script" -Level Info
Write-Log -Message "========================================" -Level Info
Write-Log -Message "Log File: $logFile" -Level Info
Write-Log -Message "Script Version: 1.0.0" -Level Info
Write-Log -Message "Execution Time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Level Info
Write-Log -Message "Mode: $Mode" -Level Info
Write-Log -Message "Auto Install: $AutoInstall" -Level Info
Write-Log -Message "----------------------------------------" -Level Info

# Log system environment
try {
    $osInfo = Get-CimInstance -ClassName Win32_OperatingSystem
    $computerInfo = Get-CimInstance -ClassName Win32_ComputerSystem

    Write-Log -Message "OS: $($osInfo.Caption) (Build $($osInfo.BuildNumber))" -Level Debug -NoConsole
    Write-Log -Message "Computer: $($computerInfo.Name)" -Level Debug -NoConsole
    Write-Log -Message "User: $env:USERNAME" -Level Debug -NoConsole
    Write-Log -Message "PowerShell Version: $($PSVersionTable.PSVersion)" -Level Debug -NoConsole
    Write-Log -Message "Script Path: $PSScriptRoot" -Level Debug -NoConsole
    Write-Log -Message "Working Directory: $PWD" -Level Debug -NoConsole
} catch {
    Write-ErrorLog -ErrorRecord $_ -Context "System Information Gathering"
}

# Requires -RunAsAdministrator check
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Write-Log -Message "Administrator Privileges: $isAdmin" -Level Info

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Memory Timeline - Dependency Setup" -ForegroundColor Cyan
Write-Host "Windows 11 Setup Script" -ForegroundColor Cyan
Write-Host "Mode: $Mode" -ForegroundColor Cyan
Write-Host "Log File: $logFile" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# Track installation status
$script:installationNeeded = $false
$script:installationFailed = $false
$script:errorCount = 0
$script:warningCount = 0

# Function to check if a command exists
function Test-CommandExists {
    param([string]$Command)
    $exists = $null -ne (Get-Command $Command -ErrorAction SilentlyContinue)
    Write-Log -Message "Checking for command '$Command': $exists" -Level Debug -NoConsole
    return $exists
}

# Function to get version of a command
function Get-CommandVersion {
    param(
        [string]$Command,
        [string]$VersionFlag = '--version'
    )

    try {
        Write-Log -Message "Getting version for '$Command' using flag '$VersionFlag'" -Level Debug -NoConsole
        $output = & $Command $VersionFlag 2>&1 | Select-Object -First 1
        $version = $output -replace '[^0-9.]', '' -replace '^\.', '' -replace '\.$', ''
        Write-Log -Message "Version detected for '$Command': $version" -Level Debug -NoConsole
        return $version
    } catch {
        Write-Log -Message "Failed to get version for '$Command': $_" -Level Warning -NoConsole
        Write-ErrorLog -ErrorRecord $_ -Context "Get-CommandVersion for $Command"
        return $null
    }
}

# Function to compare versions
function Compare-Version {
    param(
        [string]$Current,
        [string]$Required
    )

    try {
        $currentVer = [version]($Current -replace '[^0-9.].*$', '')
        $requiredVer = [version]($Required -replace '[^0-9.].*$', '')
        $result = $currentVer -ge $requiredVer
        Write-Log -Message "Version comparison: $Current >= $Required = $result" -Level Debug -NoConsole
        return $result
    } catch {
        Write-Log -Message "Version comparison failed: $Current vs $Required" -Level Warning -NoConsole
        Write-ErrorLog -ErrorRecord $_ -Context "Compare-Version"
        return $false
    }
}

# Function to install via winget
function Install-ViaWinget {
    param(
        [string]$PackageId,
        [string]$Name
    )

    Write-Log -Message "Starting installation of '$Name' (Package ID: $PackageId) via winget" -Level Info
    Write-Host "Installing $Name via winget..." -ForegroundColor Yellow

    try {
        $installOutput = winget install --id $PackageId --silent --accept-source-agreements --accept-package-agreements 2>&1
        Write-Log -Message "Winget output for '$Name':`n$installOutput" -Level Debug -NoConsole

        if ($LASTEXITCODE -eq 0) {
            Write-Log -Message "Successfully installed '$Name' via winget" -Level Success
            Write-Host "✓ $Name installed successfully" -ForegroundColor Green
            return $true
        } else {
            $script:errorCount++
            Write-Log -Message "Failed to install '$Name' - Exit code: $LASTEXITCODE" -Level Error
            Write-Log -Message "Winget error output: $installOutput" -Level Error -NoConsole
            Write-Host "✗ Failed to install $Name" -ForegroundColor Red
            return $false
        }
    } catch {
        $script:errorCount++
        Write-ErrorLog -ErrorRecord $_ -Context "Install-ViaWinget for $Name"
        Write-Host "✗ Error installing $Name : $_" -ForegroundColor Red
        return $false
    }
}

# Function to prompt for manual installation
function Invoke-ManualInstall {
    param(
        [string]$Name,
        [string]$Url
    )

    Write-Host "`n$Name is not installed." -ForegroundColor Yellow
    Write-Host "Please download and install from: $Url" -ForegroundColor Yellow

    $response = Read-Host "Press Enter after installation is complete, or 'S' to skip"
    return $response -ne 'S'
}

# ============================================
# Check 1: Node.js
# ============================================
Write-Host "`n[1/5] Checking Node.js..." -ForegroundColor Cyan
Write-Log -Message "[1/5] Checking Node.js..." -Level Info

$nodeInstalled = Test-CommandExists 'node'
if ($nodeInstalled) {
    $nodeVersion = Get-CommandVersion 'node' '-v'
    $nodeVersionClean = $nodeVersion -replace '^v', ''
    Write-Host "✓ Node.js $nodeVersion found" -ForegroundColor Green
    Write-Log -Message "Node.js $nodeVersion found" -Level Success

    # Check if version meets requirement (16+)
    if (-not (Compare-Version $nodeVersionClean '16.0.0')) {
        $script:warningCount++
        Write-Host "⚠ Node.js version $nodeVersion is below recommended 16.0.0" -ForegroundColor Yellow
        Write-Host "  Please update Node.js manually from https://nodejs.org" -ForegroundColor Yellow
        Write-Log -Message "Node.js version $nodeVersion is below recommended 16.0.0" -Level Warning
    }
} else {
    Write-Host "✗ Node.js not found" -ForegroundColor Red
    Write-Log -Message "Node.js not found" -Level Error
    $script:installationNeeded = $true

    if ($AutoInstall) {
        if (Test-CommandExists 'winget') {
            $success = Install-ViaWinget 'OpenJS.NodeJS.LTS' 'Node.js LTS'
            if (-not $success) { $script:installationFailed = $true }
        } else {
            $script:warningCount++
            Write-Host "⚠ winget not available. Please install Node.js manually." -ForegroundColor Yellow
            Write-Log -Message "winget not available - manual Node.js installation required" -Level Warning
            Invoke-ManualInstall 'Node.js' 'https://nodejs.org/en/download/'
        }
    } else {
        if (Test-CommandExists 'winget') {
            $response = Read-Host "Install Node.js LTS via winget? (Y/n)"
            Write-Log -Message "User response to install Node.js: $response" -Level Debug -NoConsole
            if ($response -ne 'n') {
                $success = Install-ViaWinget 'OpenJS.NodeJS.LTS' 'Node.js LTS'
                if (-not $success) { $script:installationFailed = $true }
            }
        } else {
            Write-Log -Message "winget not available - prompting for manual installation" -Level Warning
            Invoke-ManualInstall 'Node.js' 'https://nodejs.org/en/download/'
        }
    }
}

# ============================================
# Check 2: npm
# ============================================
Write-Host "`n[2/5] Checking npm..." -ForegroundColor Cyan
Write-Log -Message "[2/5] Checking npm..." -Level Info

$npmInstalled = Test-CommandExists 'npm'
if ($npmInstalled) {
    $npmVersion = Get-CommandVersion 'npm'
    Write-Host "✓ npm $npmVersion found" -ForegroundColor Green
    Write-Log -Message "npm $npmVersion found" -Level Success
} else {
    $script:errorCount++
    Write-Host "✗ npm not found" -ForegroundColor Red
    Write-Host "  npm is typically installed with Node.js" -ForegroundColor Yellow
    Write-Host "  If you just installed Node.js, please restart your terminal" -ForegroundColor Yellow
    Write-Log -Message "npm not found - may require terminal restart" -Level Error
    $script:installationFailed = $true
}

# ============================================
# Check 3: Python (for node-gyp/native modules)
# ============================================
Write-Host "`n[3/5] Checking Python..." -ForegroundColor Cyan
Write-Log -Message "[3/5] Checking Python..." -Level Info

$pythonInstalled = $false
$pythonCommands = @('python', 'python3', 'py')

foreach ($cmd in $pythonCommands) {
    if (Test-CommandExists $cmd) {
        $pythonVersion = Get-CommandVersion $cmd '--version'
        Write-Host "✓ Python $pythonVersion found ($cmd)" -ForegroundColor Green
        Write-Log -Message "Python $pythonVersion found ($cmd)" -Level Success
        $pythonInstalled = $true
        break
    }
}

if (-not $pythonInstalled) {
    Write-Host "✗ Python not found" -ForegroundColor Red
    Write-Host "  Python is required for building native Node.js modules (better-sqlite3)" -ForegroundColor Yellow
    Write-Log -Message "Python not found - required for native Node.js modules" -Level Error
    $script:installationNeeded = $true

    if ($AutoInstall) {
        if (Test-CommandExists 'winget') {
            $success = Install-ViaWinget 'Python.Python.3.12' 'Python 3.12'
            if (-not $success) { $script:installationFailed = $true }
        } else {
            $script:warningCount++
            Write-Host "⚠ winget not available. Please install Python manually." -ForegroundColor Yellow
            Write-Log -Message "winget not available - manual Python installation required" -Level Warning
            Invoke-ManualInstall 'Python' 'https://www.python.org/downloads/'
        }
    } else {
        if (Test-CommandExists 'winget') {
            $response = Read-Host "Install Python 3.12 via winget? (Y/n)"
            Write-Log -Message "User response to install Python: $response" -Level Debug -NoConsole
            if ($response -ne 'n') {
                $success = Install-ViaWinget 'Python.Python.3.12' 'Python 3.12'
                if (-not $success) { $script:installationFailed = $true }
            }
        } else {
            Write-Log -Message "winget not available - prompting for manual Python installation" -Level Warning
            Invoke-ManualInstall 'Python' 'https://www.python.org/downloads/'
        }
    }
}

# ============================================
# Check 4: Visual Studio Build Tools (for native compilation)
# ============================================
Write-Host "`n[4/5] Checking Build Tools..." -ForegroundColor Cyan
Write-Log -Message "[4/5] Checking Visual Studio Build Tools..." -Level Info

# Check for Visual Studio or Build Tools
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$buildToolsInstalled = $false
Write-Log -Message "Checking for vswhere at: $vswhere" -Level Debug -NoConsole

if (Test-Path $vswhere) {
    Write-Log -Message "vswhere.exe found, querying for VC tools" -Level Debug -NoConsole
    $vsInstalls = & $vswhere -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ($vsInstalls) {
        Write-Host "✓ Visual Studio Build Tools found" -ForegroundColor Green
        Write-Log -Message "Visual Studio Build Tools found at: $vsInstalls" -Level Success
        $buildToolsInstalled = $true
    }
} else {
    Write-Log -Message "vswhere.exe not found" -Level Debug -NoConsole
}

if (-not $buildToolsInstalled) {
    $script:warningCount++
    Write-Host "✗ Visual Studio Build Tools not found" -ForegroundColor Red
    Write-Host "  Required for compiling native Node.js modules (better-sqlite3)" -ForegroundColor Yellow
    Write-Log -Message "Visual Studio Build Tools not found - required for native modules" -Level Warning
    $script:installationNeeded = $true

    if ($AutoInstall) {
        $script:warningCount++
        Write-Host "⚠ Build Tools require manual installation" -ForegroundColor Yellow
        Write-Host "  Download from: https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022" -ForegroundColor Yellow
        Write-Host "  Required components: Desktop development with C++" -ForegroundColor Yellow
        Write-Log -Message "Build Tools require manual installation (not available via winget)" -Level Warning
    } else {
        Write-Host "`nVisual Studio Build Tools installation required." -ForegroundColor Yellow
        Write-Host "Download from: https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022" -ForegroundColor Yellow
        Write-Host "Required components: Desktop development with C++" -ForegroundColor Yellow
        Write-Log -Message "Prompting user for manual Build Tools installation" -Level Info
        Read-Host "`nPress Enter after installation is complete"
    }
}

# ============================================
# Check 5: Git (optional but recommended)
# ============================================
Write-Host "`n[5/5] Checking Git..." -ForegroundColor Cyan
Write-Log -Message "[5/5] Checking Git..." -Level Info

$gitInstalled = Test-CommandExists 'git'
if ($gitInstalled) {
    $gitVersion = Get-CommandVersion 'git' '--version'
    Write-Host "✓ Git $gitVersion found" -ForegroundColor Green
    Write-Log -Message "Git $gitVersion found" -Level Success
} else {
    $script:warningCount++
    Write-Host "⚠ Git not found (optional but recommended)" -ForegroundColor Yellow
    Write-Log -Message "Git not found (optional but recommended)" -Level Warning

    if ($AutoInstall) {
        if (Test-CommandExists 'winget') {
            $success = Install-ViaWinget 'Git.Git' 'Git'
        }
    } else {
        if (Test-CommandExists 'winget') {
            $response = Read-Host "Install Git via winget? (Y/n)"
            Write-Log -Message "User response to install Git: $response" -Level Debug -NoConsole
            if ($response -ne 'n') {
                Install-ViaWinget 'Git.Git' 'Git'
            }
        }
    }
}

# ============================================
# Install npm dependencies
# ============================================
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Installing npm dependencies..." -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan
Write-Log -Message "========================================" -Level Info
Write-Log -Message "Starting npm dependency installation" -Level Info
Write-Log -Message "========================================" -Level Info

if ($npmInstalled -and (Test-Path 'package.json')) {
    Write-Host "Running npm install..." -ForegroundColor Yellow
    Write-Log -Message "npm is available and package.json found" -Level Info

    # Log npm environment
    try {
        $npmPrefix = npm config get prefix 2>&1
        $npmCache = npm config get cache 2>&1
        $npmRegistry = npm config get registry 2>&1
        Write-Log -Message "npm prefix: $npmPrefix" -Level Debug -NoConsole
        Write-Log -Message "npm cache: $npmCache" -Level Debug -NoConsole
        Write-Log -Message "npm registry: $npmRegistry" -Level Debug -NoConsole
    } catch {
        Write-Log -Message "Could not retrieve npm configuration" -Level Warning -NoConsole
    }

    # Set npm config for better-sqlite3 compilation on Windows
    Write-Log -Message "Configuring npm for native module compilation (msvs_version=2022)" -Level Info
    try {
        npm config set msvs_version 2022 2>&1 | Out-Null
        Write-Log -Message "npm config set msvs_version 2022 - Success" -Level Success
    } catch {
        Write-Log -Message "Failed to set npm msvs_version config" -Level Warning
        Write-ErrorLog -ErrorRecord $_ -Context "npm config set"
    }

    # Log package.json location and content summary
    $packageJsonPath = Resolve-Path 'package.json'
    Write-Log -Message "package.json path: $packageJsonPath" -Level Debug -NoConsole
    try {
        $packageJson = Get-Content 'package.json' -Raw | ConvertFrom-Json
        Write-Log -Message "Project name: $($packageJson.name)" -Level Debug -NoConsole
        Write-Log -Message "Project version: $($packageJson.version)" -Level Debug -NoConsole
        if ($packageJson.dependencies) {
            $depCount = ($packageJson.dependencies.PSObject.Properties | Measure-Object).Count
            Write-Log -Message "Dependencies count: $depCount" -Level Debug -NoConsole
        }
        if ($packageJson.devDependencies) {
            $devDepCount = ($packageJson.devDependencies.PSObject.Properties | Measure-Object).Count
            Write-Log -Message "Dev dependencies count: $devDepCount" -Level Debug -NoConsole
        }
    } catch {
        Write-Log -Message "Could not parse package.json for metadata" -Level Warning -NoConsole
    }

    try {
        Write-Log -Message "Starting npm install (Mode: $Mode)" -Level Info

        # Capture npm output
        $npmStartTime = Get-Date
        if ($Mode -eq 'prod') {
            Write-Log -Message "Running: npm install --production --no-optional" -Level Info
            $npmOutput = npm install --production --no-optional 2>&1
        } else {
            Write-Log -Message "Running: npm install" -Level Info
            $npmOutput = npm install 2>&1
        }
        $npmEndTime = Get-Date
        $npmDuration = ($npmEndTime - $npmStartTime).TotalSeconds

        # Log full npm output
        Write-Log -Message "npm install output:`n$npmOutput" -Level Debug -NoConsole
        Write-Log -Message "npm install duration: $npmDuration seconds" -Level Info

        if ($LASTEXITCODE -eq 0) {
            Write-Host "`n✓ npm dependencies installed successfully" -ForegroundColor Green
            Write-Log -Message "npm install completed successfully (Exit code: 0)" -Level Success

            # Log installed packages
            try {
                $nodeModulesPath = Join-Path $PWD "node_modules"
                if (Test-Path $nodeModulesPath) {
                    $installedPackages = (Get-ChildItem $nodeModulesPath -Directory | Measure-Object).Count
                    Write-Log -Message "Installed packages in node_modules: $installedPackages" -Level Info
                }
            } catch {
                Write-Log -Message "Could not count installed packages" -Level Debug -NoConsole
            }
        } else {
            $script:errorCount++
            Write-Host "`n✗ npm install failed with exit code $LASTEXITCODE" -ForegroundColor Red
            Write-Host "  If you're behind a proxy or have network issues, try:" -ForegroundColor Yellow
            Write-Host "  npm install --ignore-scripts" -ForegroundColor Yellow

            Write-Log -Message "npm install FAILED with exit code: $LASTEXITCODE" -Level Error
            Write-Log -Message "npm error output:`n$npmOutput" -Level Error

            # Try to extract specific error information
            $errorLines = $npmOutput | Select-String -Pattern "ERR!" -AllMatches
            if ($errorLines) {
                Write-Log -Message "npm error lines:`n$($errorLines -join "`n")" -Level Error
            }

            # Check for common issues
            if ($npmOutput -match "EACCES|permission denied") {
                Write-Log -Message "Detected permission issue in npm install" -Level Error
                Write-Host "  Permission issue detected - try running as Administrator" -ForegroundColor Yellow
            }
            if ($npmOutput -match "ETIMEDOUT|ENOTFOUND|network") {
                Write-Log -Message "Detected network issue in npm install" -Level Error
                Write-Host "  Network issue detected - check your internet connection" -ForegroundColor Yellow
            }
            if ($npmOutput -match "gyp ERR!|node-gyp") {
                Write-Log -Message "Detected node-gyp compilation error" -Level Error
                Write-Host "  Native module compilation failed - ensure Build Tools are installed" -ForegroundColor Yellow
            }

            $script:installationFailed = $true
        }
    } catch {
        $script:errorCount++
        Write-Host "`n✗ Error during npm install: $_" -ForegroundColor Red
        Write-ErrorLog -ErrorRecord $_ -Context "npm install execution"
        $script:installationFailed = $true
    }
} else {
    if (-not (Test-Path 'package.json')) {
        $script:warningCount++
        Write-Host "⚠ package.json not found in current directory" -ForegroundColor Yellow
        Write-Host "  Please run this script from the project root directory" -ForegroundColor Yellow
        Write-Log -Message "package.json not found in current directory: $PWD" -Level Warning
    }
    if (-not $npmInstalled) {
        Write-Log -Message "Skipping npm install - npm is not available" -Level Warning
    }
}

# ============================================
# Summary
# ============================================
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Setup Summary" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

Write-Log -Message "========================================" -Level Info
Write-Log -Message "Setup Summary" -Level Info
Write-Log -Message "========================================" -Level Info
Write-Log -Message "Errors: $script:errorCount" -Level Info
Write-Log -Message "Warnings: $script:warningCount" -Level Info
Write-Log -Message "Installation needed: $script:installationNeeded" -Level Info
Write-Log -Message "Installation failed: $script:installationFailed" -Level Info

if ($script:installationFailed) {
    Write-Host "⚠ Setup completed with errors" -ForegroundColor Yellow
    Write-Host "Please review the messages above and install missing dependencies manually." -ForegroundColor Yellow
    Write-Host "`nCommon issues:" -ForegroundColor Yellow
    Write-Host "  1. Restart your terminal/PowerShell after installing Node.js" -ForegroundColor Yellow
    Write-Host "  2. Visual Studio Build Tools require ~6GB download and manual installation" -ForegroundColor Yellow
    Write-Host "  3. Python must be added to PATH during installation" -ForegroundColor Yellow
    Write-Host "`nLog file location:" -ForegroundColor Cyan
    Write-Host "  $logFile" -ForegroundColor White

    Write-Log -Message "Setup completed with errors - see log for details" -Level Error
    Write-Log -Message "Script execution completed at: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Level Info
    Write-Log -Message "========================================" -Level Info
    exit 1
} elseif ($script:installationNeeded) {
    Write-Host "ℹ Some dependencies were installed" -ForegroundColor Cyan
    Write-Host "You may need to restart your terminal for changes to take effect." -ForegroundColor Cyan
    Write-Host "`nNext steps:" -ForegroundColor Cyan
    Write-Host "  1. Close and reopen your terminal" -ForegroundColor Cyan
    Write-Host "  2. Run this script again to verify installation" -ForegroundColor Cyan
    Write-Host "  3. Run 'npm run dev' to start development mode" -ForegroundColor Cyan
    Write-Host "`nLog file location:" -ForegroundColor Cyan
    Write-Host "  $logFile" -ForegroundColor White

    Write-Log -Message "Some dependencies were installed - terminal restart may be required" -Level Info
    Write-Log -Message "Script execution completed at: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Level Info
    Write-Log -Message "========================================" -Level Info
    exit 0
} else {
    Write-Host "✓ All dependencies are installed!" -ForegroundColor Green
    Write-Host "`nYou're ready to start developing!" -ForegroundColor Green
    Write-Host "`nNext steps:" -ForegroundColor Cyan
    Write-Host "  Development: npm run dev" -ForegroundColor Cyan
    Write-Host "  Build:       npm run build" -ForegroundColor Cyan
    Write-Host "  Package:     npm run package" -ForegroundColor Cyan
    Write-Host "  Test:        npm test" -ForegroundColor Cyan
    Write-Host "`nLog file location:" -ForegroundColor Cyan
    Write-Host "  $logFile" -ForegroundColor White

    Write-Log -Message "All dependencies installed successfully" -Level Success
    Write-Log -Message "Script execution completed at: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -Level Info
    Write-Log -Message "========================================" -Level Info
    exit 0
}
