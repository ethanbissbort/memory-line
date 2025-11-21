# Memory Timeline - PowerShell Scripts

This directory contains PowerShell scripts for setting up, verifying, and managing the Memory Timeline Windows native application.

## Scripts

### Setup-Dependencies.ps1

Automatically checks and installs all required dependencies for development or production use.

**Usage:**

```powershell
# Full development setup (requires Administrator)
.\Setup-Dependencies.ps1 -Mode Development

# Production runtime only
.\Setup-Dependencies.ps1 -Mode Production

# Skip optional components
.\Setup-Dependencies.ps1 -Mode Development -SkipOptional
```

**What it installs:**

**Core (Both Development and Production):**
- .NET 8 SDK
- Windows App SDK 1.5+
- Windows Package Manager (winget)

**Development Only:**
- Visual Studio 2022 (Community or higher)
- Windows SDK (10.0.22621.0+)
- Git for Windows

**Optional (Development):**
- MSIX Packaging Tool
- Visual Studio Code

**Features:**
- ✓ Automatic version detection
- ✓ Automatic installation of missing components
- ✓ NuGet package restoration
- ✓ Environment variable updates
- ✓ Comprehensive error handling
- ✓ Interactive prompts for optional components
- ✓ Colored output for easy reading

**Requirements:**
- Windows 11 21H2 or later (22H2 recommended)
- Administrator privileges
- Internet connection

### Verify-Installation.ps1

Verifies that all dependencies are correctly installed and configured.

**Usage:**

```powershell
# Basic verification
.\Verify-Installation.ps1

# Detailed information
.\Verify-Installation.ps1 -Detailed

# Export report to file
.\Verify-Installation.ps1 -ExportReport
```

**What it checks:**

**System Requirements:**
- Windows version (11 21H2+)
- System RAM (8GB+ recommended)
- Free disk space (10GB+ recommended)
- CPU information

**Development Tools:**
- .NET SDK (8.0+)
- Visual Studio 2022 (17.8+)
- Windows SDK (10.0.22621.0+)
- Windows App SDK (1.5+)
- Git
- PowerShell (5.0+)

**Optional Tools:**
- Windows Package Manager (winget)
- Visual Studio Code
- MSIX Packaging Tool

**Application:**
- Memory Timeline MSIX package installation

**Features:**
- ✓ Comprehensive dependency verification
- ✓ Version checking
- ✓ Detailed component information
- ✓ Pass/Fail/Warning status for each check
- ✓ Summary statistics
- ✓ Optional report export
- ✓ Exit codes for CI/CD integration

**Exit Codes:**
- `0`: All checks passed
- `> 0`: Number of failed checks

## Common Scenarios

### First-Time Setup (Developer)

```powershell
# 1. Open PowerShell as Administrator
# 2. Navigate to scripts directory
cd memory-line\windows-native\scripts

# 3. Run setup script
.\Setup-Dependencies.ps1 -Mode Development

# 4. Verify installation
.\Verify-Installation.ps1 -Detailed

# 5. Open Visual Studio and build
cd ..\src
start MemoryTimeline.sln
```

### Production Runtime Setup

```powershell
# 1. Open PowerShell as Administrator
# 2. Navigate to scripts directory
cd memory-line\windows-native\scripts

# 3. Install runtime dependencies only
.\Setup-Dependencies.ps1 -Mode Production

# 4. Verify installation
.\Verify-Installation.ps1
```

### Troubleshooting Installation Issues

```powershell
# 1. Check what's missing
.\Verify-Installation.ps1 -Detailed

# 2. Re-run setup to fix missing components
.\Setup-Dependencies.ps1 -Mode Development

# 3. Export detailed report
.\Verify-Installation.ps1 -Detailed -ExportReport

# 4. Check verification-report.txt for details
notepad verification-report.txt
```

### CI/CD Integration

```powershell
# In your CI/CD pipeline (GitHub Actions, Azure DevOps, etc.)

# Verify environment
.\scripts\Verify-Installation.ps1
if ($LASTEXITCODE -ne 0) {
    Write-Error "Dependency verification failed"
    exit 1
}

# Build and test
cd src
dotnet build --configuration Release
dotnet test --configuration Release
```

## Manual Installation

If automatic installation fails, you can install components manually:

### .NET 8 SDK
Download from: https://dotnet.microsoft.com/download/dotnet/8.0

### Visual Studio 2022
Download from: https://visualstudio.microsoft.com/downloads/

Required workloads:
- .NET desktop development
- Universal Windows Platform development
- Windows App SDK C# Templates

### Windows SDK
Download from: https://developer.microsoft.com/windows/downloads/windows-sdk/

### Windows App SDK
Install via Visual Studio Installer or download from:
https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads

### Git for Windows
Download from: https://git-scm.com/download/win

## Script Permissions

If you encounter execution policy errors:

```powershell
# Check current execution policy
Get-ExecutionPolicy

# Temporarily bypass for current session
Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process

# Or set for current user (more permanent)
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

## Getting Help

For issues with the scripts:

```powershell
# View script help
Get-Help .\Setup-Dependencies.ps1 -Full
Get-Help .\Verify-Installation.ps1 -Full

# View examples
Get-Help .\Setup-Dependencies.ps1 -Examples
```

For Memory Timeline issues:
- GitHub Issues: https://github.com/yourusername/memory-line/issues
- Documentation: See `../DEPLOYMENT.md`

## Notes

- **Administrator privileges required**: Both scripts require admin rights to install components and modify system settings
- **Internet connection required**: For downloading and installing dependencies
- **Windows 11 only**: These scripts are designed for Windows 11 (build 22000+)
- **Execution time**: Initial setup may take 15-30 minutes depending on what needs to be installed
- **Restart recommended**: After installation, restart your computer to ensure all environment variables are updated

## Version History

- **1.0.0** (2024-11-21): Initial release
  - Setup-Dependencies.ps1: Automated dependency installation
  - Verify-Installation.ps1: Comprehensive verification

---

**Last Updated**: 2024-11-21
