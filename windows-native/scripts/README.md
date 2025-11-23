# Windows Native Setup Scripts

This directory contains PowerShell scripts for setting up the development and production environment for the Memory Timeline Windows native application.

## Available Scripts

### 1. Setup-Dependencies.ps1

**Purpose:** Basic dependency verification and installation script.

**Features:**
- Checks for required dependencies
- Automated installation of missing components
- Support for Development and Production modes
- Interactive and automated modes

**Usage:**
```powershell
# Run as Administrator
.\Setup-Dependencies.ps1 -Mode Development

# Production mode only
.\Setup-Dependencies.ps1 -Mode Production

# Skip optional components
.\Setup-Dependencies.ps1 -Mode Development -SkipOptional
```

**Requirements:**
- Windows 11 21H2 or later
- Administrator privileges

---

### 2. Setup-Dependencies-Enhanced.ps1 ⭐ RECOMMENDED

**Purpose:** Enhanced dependency setup with robust error handling, comprehensive logging, and advanced recovery mechanisms.

**Enhanced Features:**
- ✅ **Comprehensive Logging**: Detailed logs to file with timestamps and error traces
- ✅ **Robust Error Handling**: Try-catch blocks with detailed error information
- ✅ **Retry Logic**: Automatic retry for failed network operations (configurable)
- ✅ **Transaction Tracking**: Installation history for audit and rollback
- ✅ **Progress Tracking**: Real-time status updates and validation
- ✅ **Non-Interactive Mode**: Fully automated execution for CI/CD pipelines
- ✅ **Detailed Validation**: CPU capability checks (NPU detection), version validation
- ✅ **Recovery Mechanisms**: Fallback options for failed installations

**Usage:**

Basic usage:
```powershell
# Development mode with default settings
.\Setup-Dependencies-Enhanced.ps1 -Mode Development

# Production runtime only
.\Setup-Dependencies-Enhanced.ps1 -Mode Production
```

Advanced options:
```powershell
# Custom log path
.\Setup-Dependencies-Enhanced.ps1 -Mode Development -LogPath "C:\Logs\setup.log"

# Increase retry count for unreliable networks
.\Setup-Dependencies-Enhanced.ps1 -Mode Development -RetryCount 5

# Non-interactive mode (for automation/CI/CD)
.\Setup-Dependencies-Enhanced.ps1 -Mode Development -NoInteractive

# Skip optional tools
.\Setup-Dependencies-Enhanced.ps1 -Mode Development -SkipOptional

# Combine options
.\Setup-Dependencies-Enhanced.ps1 -Mode Development -RetryCount 5 -NoInteractive -LogPath "C:\BuildLogs\setup.log"
```

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `-Mode` | String | Development | 'Development' or 'Production' |
| `-SkipOptional` | Switch | False | Skip installation of optional components |
| `-LogPath` | String | Auto-generated | Custom path for log file |
| `-RetryCount` | Int | 3 | Number of retries for failed operations (1-10) |
| `-NoInteractive` | Switch | False | Run without user prompts (auto-accept) |

**Log Files:**

Logs are automatically created in `windows-native/logs/` with the format:
```
setup-YYYYMMDD-HHMMSS.log
```

Example: `setup-20251123-143052.log`

**Requirements:**
- Windows 11 21H2 or later (22H2+ recommended)
- Administrator privileges
- PowerShell 5.1 or later

---

## What Gets Installed

### Core Components (Required for both modes)

- ✅ Windows 11 validation
- ✅ .NET 8 SDK
- ✅ Windows App SDK 1.5+
- ✅ PowerShell 5.1+
- ✅ winget (Windows Package Manager)

### Development Mode Additional Components

- ✅ Visual Studio 2022 (17.8+) - *Manual installation required*
  - .NET desktop development workload
  - Universal Windows Platform development
  - Windows App SDK C# Templates
- ✅ Windows SDK (10.0.22621.0+)
- ✅ Git for Windows
- ✅ NuGet package restoration
- 📦 Visual Studio Code (optional)
- 📦 MSIX Packaging Tool (optional)

### Production Mode Components

- ✅ Runtime dependencies only
- ✅ Windows App SDK runtime
- ✅ No development tools

---

## Comparison: Basic vs Enhanced Script

| Feature | Setup-Dependencies.ps1 | Setup-Dependencies-Enhanced.ps1 |
|---------|----------------------|--------------------------------|
| Basic validation | ✅ | ✅ |
| Automated installation | ✅ | ✅ |
| Console output | ✅ | ✅ |
| **File logging** | ❌ | ✅ Comprehensive |
| **Error stack traces** | ❌ | ✅ Detailed |
| **Retry logic** | ❌ | ✅ Configurable (1-10 retries) |
| **Transaction history** | ❌ | ✅ Full audit trail |
| **Non-interactive mode** | ❌ | ✅ CI/CD ready |
| **NPU detection** | ❌ | ✅ Intel/AMD NPU |
| **Detailed version checks** | Basic | ✅ Comprehensive |
| **Custom log paths** | ❌ | ✅ Configurable |
| **Progress tracking** | Basic | ✅ Detailed |
| **Recovery mechanisms** | Basic | ✅ Advanced |

---

## Troubleshooting

### Script Requires Administrator

**Error:** "This script requires Administrator privileges"

**Solution:**
1. Right-click PowerShell
2. Select "Run as Administrator"
3. Run the script again

### Execution Policy Error

**Error:** "script cannot be loaded because running scripts is disabled"

**Solution:**
```powershell
# Allow scripts for current user
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser

# Or for current session only
Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process
```

### Download Failures

If downloads fail repeatedly:
1. Check internet connection
2. Check firewall/proxy settings
3. Increase retry count: `-RetryCount 5`
4. Download components manually

### Visual Studio Not Detected

The script cannot automatically install Visual Studio 2022. You must:
1. Download from https://visualstudio.microsoft.com/downloads/
2. Install with required workloads
3. Re-run the script

### Log File Location

Enhanced script logs are saved to:
```
windows-native\logs\setup-YYYYMMDD-HHMMSS.log
```

Use `-LogPath` parameter to specify custom location.

---

## CI/CD Integration

For automated builds and CI/CD pipelines, use the enhanced script in non-interactive mode:

```powershell
# GitHub Actions / Azure Pipelines
.\Setup-Dependencies-Enhanced.ps1 `
    -Mode Development `
    -NoInteractive `
    -SkipOptional `
    -LogPath "$env:BUILD_ARTIFACTSTAGINGDIRECTORY\setup.log" `
    -RetryCount 5
```

Check exit codes:
- `0` - Success
- `1` - Failure (check log for details)

---

## Validation After Setup

After running the setup script, validate the installation:

### Check .NET SDK
```powershell
dotnet --version
dotnet --list-sdks
```

### Check Visual Studio
```powershell
& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest
```

### Verify Windows SDK
```powershell
Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Directory
```

### Test Project Build
```powershell
cd ..\src
dotnet restore
dotnet build
```

---

## Support

If you encounter issues:

1. **Check the log file** - Enhanced script provides detailed logs
2. **Review error messages** - Look for specific error codes
3. **Manual installation** - Some components require manual installation
4. **Re-run script** - After manual fixes, re-run to validate
5. **Report issues** - Create a GitHub issue with log file

---

## Version History

### Version 2.0.0 - Enhanced Script (2025-11-23)
- Added comprehensive file logging
- Implemented retry logic for network operations
- Added transaction tracking and audit trail
- Non-interactive mode for CI/CD
- NPU capability detection
- Detailed error handling with stack traces
- Configurable retry count
- Custom log path support

### Version 1.0.0 - Basic Script
- Initial release
- Basic dependency checking
- Automated installation support
- Development and Production modes

---

## License

MIT License - See main repository LICENSE file.

---

**Last Updated:** 2025-11-23
**Maintained By:** Memory Timeline Development Team
