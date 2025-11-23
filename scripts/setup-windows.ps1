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

# Requires -RunAsAdministrator check
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Memory Timeline - Dependency Setup" -ForegroundColor Cyan
Write-Host "Windows 11 Setup Script" -ForegroundColor Cyan
Write-Host "Mode: $Mode" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# Track installation status
$script:installationNeeded = $false
$script:installationFailed = $false

# Function to check if a command exists
function Test-CommandExists {
    param([string]$Command)
    $null -ne (Get-Command $Command -ErrorAction SilentlyContinue)
}

# Function to get version of a command
function Get-CommandVersion {
    param(
        [string]$Command,
        [string]$VersionFlag = '--version'
    )

    try {
        $output = & $Command $VersionFlag 2>&1 | Select-Object -First 1
        return $output -replace '[^0-9.]', '' -replace '^\.', '' -replace '\.$', ''
    } catch {
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
        return $currentVer -ge $requiredVer
    } catch {
        return $false
    }
}

# Function to install via winget
function Install-ViaWinget {
    param(
        [string]$PackageId,
        [string]$Name
    )

    Write-Host "Installing $Name via winget..." -ForegroundColor Yellow
    try {
        winget install --id $PackageId --silent --accept-source-agreements --accept-package-agreements
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✓ $Name installed successfully" -ForegroundColor Green
            return $true
        } else {
            Write-Host "✗ Failed to install $Name" -ForegroundColor Red
            return $false
        }
    } catch {
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

$nodeInstalled = Test-CommandExists 'node'
if ($nodeInstalled) {
    $nodeVersion = Get-CommandVersion 'node' '-v'
    $nodeVersionClean = $nodeVersion -replace '^v', ''
    Write-Host "✓ Node.js $nodeVersion found" -ForegroundColor Green

    # Check if version meets requirement (16+)
    if (-not (Compare-Version $nodeVersionClean '16.0.0')) {
        Write-Host "⚠ Node.js version $nodeVersion is below recommended 16.0.0" -ForegroundColor Yellow
        Write-Host "  Please update Node.js manually from https://nodejs.org" -ForegroundColor Yellow
    }
} else {
    Write-Host "✗ Node.js not found" -ForegroundColor Red
    $script:installationNeeded = $true

    if ($AutoInstall) {
        if (Test-CommandExists 'winget') {
            $success = Install-ViaWinget 'OpenJS.NodeJS.LTS' 'Node.js LTS'
            if (-not $success) { $script:installationFailed = $true }
        } else {
            Write-Host "⚠ winget not available. Please install Node.js manually." -ForegroundColor Yellow
            Invoke-ManualInstall 'Node.js' 'https://nodejs.org/en/download/'
        }
    } else {
        if (Test-CommandExists 'winget') {
            $response = Read-Host "Install Node.js LTS via winget? (Y/n)"
            if ($response -ne 'n') {
                $success = Install-ViaWinget 'OpenJS.NodeJS.LTS' 'Node.js LTS'
                if (-not $success) { $script:installationFailed = $true }
            }
        } else {
            Invoke-ManualInstall 'Node.js' 'https://nodejs.org/en/download/'
        }
    }
}

# ============================================
# Check 2: npm
# ============================================
Write-Host "`n[2/5] Checking npm..." -ForegroundColor Cyan

$npmInstalled = Test-CommandExists 'npm'
if ($npmInstalled) {
    $npmVersion = Get-CommandVersion 'npm'
    Write-Host "✓ npm $npmVersion found" -ForegroundColor Green
} else {
    Write-Host "✗ npm not found" -ForegroundColor Red
    Write-Host "  npm is typically installed with Node.js" -ForegroundColor Yellow
    Write-Host "  If you just installed Node.js, please restart your terminal" -ForegroundColor Yellow
    $script:installationFailed = $true
}

# ============================================
# Check 3: Python (for node-gyp/native modules)
# ============================================
Write-Host "`n[3/5] Checking Python..." -ForegroundColor Cyan

$pythonInstalled = $false
$pythonCommands = @('python', 'python3', 'py')

foreach ($cmd in $pythonCommands) {
    if (Test-CommandExists $cmd) {
        $pythonVersion = Get-CommandVersion $cmd '--version'
        Write-Host "✓ Python $pythonVersion found ($cmd)" -ForegroundColor Green
        $pythonInstalled = $true
        break
    }
}

if (-not $pythonInstalled) {
    Write-Host "✗ Python not found" -ForegroundColor Red
    Write-Host "  Python is required for building native Node.js modules (better-sqlite3)" -ForegroundColor Yellow
    $script:installationNeeded = $true

    if ($AutoInstall) {
        if (Test-CommandExists 'winget') {
            $success = Install-ViaWinget 'Python.Python.3.12' 'Python 3.12'
            if (-not $success) { $script:installationFailed = $true }
        } else {
            Write-Host "⚠ winget not available. Please install Python manually." -ForegroundColor Yellow
            Invoke-ManualInstall 'Python' 'https://www.python.org/downloads/'
        }
    } else {
        if (Test-CommandExists 'winget') {
            $response = Read-Host "Install Python 3.12 via winget? (Y/n)"
            if ($response -ne 'n') {
                $success = Install-ViaWinget 'Python.Python.3.12' 'Python 3.12'
                if (-not $success) { $script:installationFailed = $true }
            }
        } else {
            Invoke-ManualInstall 'Python' 'https://www.python.org/downloads/'
        }
    }
}

# ============================================
# Check 4: Visual Studio Build Tools (for native compilation)
# ============================================
Write-Host "`n[4/5] Checking Build Tools..." -ForegroundColor Cyan

# Check for Visual Studio or Build Tools
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$buildToolsInstalled = $false

if (Test-Path $vswhere) {
    $vsInstalls = & $vswhere -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ($vsInstalls) {
        Write-Host "✓ Visual Studio Build Tools found" -ForegroundColor Green
        $buildToolsInstalled = $true
    }
}

if (-not $buildToolsInstalled) {
    Write-Host "✗ Visual Studio Build Tools not found" -ForegroundColor Red
    Write-Host "  Required for compiling native Node.js modules (better-sqlite3)" -ForegroundColor Yellow
    $script:installationNeeded = $true

    if ($AutoInstall) {
        Write-Host "⚠ Build Tools require manual installation" -ForegroundColor Yellow
        Write-Host "  Download from: https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022" -ForegroundColor Yellow
        Write-Host "  Required components: Desktop development with C++" -ForegroundColor Yellow
    } else {
        Write-Host "`nVisual Studio Build Tools installation required." -ForegroundColor Yellow
        Write-Host "Download from: https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022" -ForegroundColor Yellow
        Write-Host "Required components: Desktop development with C++" -ForegroundColor Yellow
        Read-Host "`nPress Enter after installation is complete"
    }
}

# ============================================
# Check 5: Git (optional but recommended)
# ============================================
Write-Host "`n[5/5] Checking Git..." -ForegroundColor Cyan

$gitInstalled = Test-CommandExists 'git'
if ($gitInstalled) {
    $gitVersion = Get-CommandVersion 'git' '--version'
    Write-Host "✓ Git $gitVersion found" -ForegroundColor Green
} else {
    Write-Host "⚠ Git not found (optional but recommended)" -ForegroundColor Yellow

    if ($AutoInstall) {
        if (Test-CommandExists 'winget') {
            $success = Install-ViaWinget 'Git.Git' 'Git'
        }
    } else {
        if (Test-CommandExists 'winget') {
            $response = Read-Host "Install Git via winget? (Y/n)"
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

if ($npmInstalled -and (Test-Path 'package.json')) {
    Write-Host "Running npm install..." -ForegroundColor Yellow

    # Set npm config for better-sqlite3 compilation on Windows
    npm config set msvs_version 2022

    try {
        if ($Mode -eq 'prod') {
            npm install --production --no-optional
        } else {
            npm install
        }

        if ($LASTEXITCODE -eq 0) {
            Write-Host "`n✓ npm dependencies installed successfully" -ForegroundColor Green
        } else {
            Write-Host "`n✗ npm install failed with exit code $LASTEXITCODE" -ForegroundColor Red
            Write-Host "  If you're behind a proxy or have network issues, try:" -ForegroundColor Yellow
            Write-Host "  npm install --ignore-scripts" -ForegroundColor Yellow
            $script:installationFailed = $true
        }
    } catch {
        Write-Host "`n✗ Error during npm install: $_" -ForegroundColor Red
        $script:installationFailed = $true
    }
} else {
    if (-not (Test-Path 'package.json')) {
        Write-Host "⚠ package.json not found in current directory" -ForegroundColor Yellow
        Write-Host "  Please run this script from the project root directory" -ForegroundColor Yellow
    }
}

# ============================================
# Summary
# ============================================
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Setup Summary" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

if ($script:installationFailed) {
    Write-Host "⚠ Setup completed with errors" -ForegroundColor Yellow
    Write-Host "Please review the messages above and install missing dependencies manually." -ForegroundColor Yellow
    Write-Host "`nCommon issues:" -ForegroundColor Yellow
    Write-Host "  1. Restart your terminal/PowerShell after installing Node.js" -ForegroundColor Yellow
    Write-Host "  2. Visual Studio Build Tools require ~6GB download and manual installation" -ForegroundColor Yellow
    Write-Host "  3. Python must be added to PATH during installation" -ForegroundColor Yellow
    exit 1
} elseif ($script:installationNeeded) {
    Write-Host "ℹ Some dependencies were installed" -ForegroundColor Cyan
    Write-Host "You may need to restart your terminal for changes to take effect." -ForegroundColor Cyan
    Write-Host "`nNext steps:" -ForegroundColor Cyan
    Write-Host "  1. Close and reopen your terminal" -ForegroundColor Cyan
    Write-Host "  2. Run this script again to verify installation" -ForegroundColor Cyan
    Write-Host "  3. Run 'npm run dev' to start development mode" -ForegroundColor Cyan
    exit 0
} else {
    Write-Host "✓ All dependencies are installed!" -ForegroundColor Green
    Write-Host "`nYou're ready to start developing!" -ForegroundColor Green
    Write-Host "`nNext steps:" -ForegroundColor Cyan
    Write-Host "  Development: npm run dev" -ForegroundColor Cyan
    Write-Host "  Build:       npm run build" -ForegroundColor Cyan
    Write-Host "  Package:     npm run package" -ForegroundColor Cyan
    Write-Host "  Test:        npm test" -ForegroundColor Cyan
    exit 0
}
