# Setup Scripts

This directory contains automated dependency verification and installation scripts for the Memory Timeline Application.

> **IMPORTANT NOTE:** This directory contains setup scripts for the **legacy Electron/Node.js version** of Memory Timeline.
>
> If you're working with the **native Windows version** (.NET + WinUI 3), please use the scripts in:
> - **`windows-native/scripts/Setup-Dependencies.ps1`** - For native Windows setup
> - **`windows-native/scripts/Verify-Installation.ps1`** - For verification
> - See [`windows-native/scripts/README.md`](../windows-native/scripts/README.md) for detailed documentation
>
> For migration guidance, see [`MIGRATION-TO-NATIVE-WIN.md`](../MIGRATION-TO-NATIVE-WIN.md)

## Available Scripts (Electron/Node.js Version)

### Windows 11 - `setup-windows.ps1`

PowerShell script to verify and install all required dependencies on Windows 11.

**Prerequisites:**
- Windows 11
- PowerShell 5.1 or later
- Administrator privileges (for installing system dependencies)

**Usage:**

```powershell
# Development mode (installs all dependencies)
.\scripts\setup-windows.ps1

# Development mode with auto-install (no prompts)
.\scripts\setup-windows.ps1 -Mode dev -AutoInstall

# Production mode (runtime dependencies only)
.\scripts\setup-windows.ps1 -Mode prod

# Get help
Get-Help .\scripts\setup-windows.ps1 -Detailed
```

**What it checks and installs:**

1. **Node.js** (v16+) - JavaScript runtime
   - Installed via winget (OpenJS.NodeJS.LTS)
   - Fallback to manual download from nodejs.org

2. **npm** - Node package manager
   - Comes bundled with Node.js

3. **Python** (v3.12+) - Required for native module compilation
   - Installed via winget (Python.Python.3.12)
   - Needed for better-sqlite3 and other native modules

4. **Visual Studio Build Tools** - C++ compiler for native modules
   - Requires manual installation
   - Download: [VS Build Tools 2022](https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022)
   - Required component: "Desktop development with C++"

5. **Git** (optional) - Version control
   - Installed via winget (Git.Git)
   - Recommended for development

**Notes:**
- You may need to restart your terminal after installing Node.js or Python
- Visual Studio Build Tools requires ~6GB download
- The script will configure npm for better-sqlite3 compilation on Windows

---

### Ubuntu 24.x - `setup-ubuntu.sh`

Bash script to verify and install all required dependencies on Ubuntu 24.04 LTS.

**Prerequisites:**
- Ubuntu 24.04 LTS (or compatible)
- sudo privileges
- Internet connection

**Usage:**

```bash
# Make script executable (first time only)
chmod +x ./scripts/setup-ubuntu.sh

# Development mode (installs all dependencies)
./scripts/setup-ubuntu.sh

# Development mode with auto-install (no prompts)
./scripts/setup-ubuntu.sh dev --auto

# Production mode (runtime dependencies only)
./scripts/setup-ubuntu.sh prod

# Help
./scripts/setup-ubuntu.sh --help
```

**What it checks and installs:**

1. **Node.js** (v20.x LTS) - JavaScript runtime
   - Installed from NodeSource repository
   - Latest LTS version

2. **npm** - Node package manager
   - Comes bundled with Node.js

3. **Python 3** - Required for native module compilation
   - Usually pre-installed on Ubuntu
   - Needed for better-sqlite3 and other native modules

4. **build-essential** - GCC, G++, Make
   - Required for compiling native C/C++ modules
   - Standard development tools

5. **Additional dependencies:**
   - `pkg-config` - Build configuration tool
   - `libsqlite3-dev` - SQLite development headers

6. **Git** (optional) - Version control
   - Recommended for development

**Notes:**
- The script will update apt package lists before installing
- You may be prompted for your sudo password
- All dependencies are installed via apt-get

---

## Common Issues & Solutions

### Windows

**Issue: "winget not found"**
- Winget comes with Windows 11 by default
- If missing, install from: https://aka.ms/getwinget
- Alternative: Install dependencies manually

**Issue: "npm install fails with python error"**
- Ensure Python is added to PATH during installation
- Restart terminal after installing Python
- Verify with: `python --version`

**Issue: "Cannot compile native modules"**
- Install Visual Studio Build Tools 2022
- Select "Desktop development with C++" workload
- Restart terminal after installation

**Issue: "Access denied"**
- Run PowerShell as Administrator
- Right-click PowerShell → "Run as Administrator"

**Issue: "Script execution is disabled"**
- Enable script execution: `Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser`
- Or run with: `powershell -ExecutionPolicy Bypass -File .\scripts\setup-windows.ps1`

### Ubuntu

**Issue: "Package not found"**
- Run: `sudo apt-get update`
- Check internet connection
- Verify /etc/apt/sources.list is correct

**Issue: "Permission denied"**
- Ensure script is executable: `chmod +x scripts/setup-ubuntu.sh`
- Run with sudo privileges when prompted

**Issue: "npm install fails"**
- Check Node.js version: `node --version` (should be 16+)
- Try with verbose output: `npm install --verbose`
- Clear npm cache: `npm cache clean --force`

**Issue: "better-sqlite3 compilation fails"**
- Install build-essential: `sudo apt-get install build-essential`
- Install libsqlite3-dev: `sudo apt-get install libsqlite3-dev`
- Ensure python3 is available: `python3 --version`

---

## Manual Installation

If automated installation fails, you can install dependencies manually:

### Windows Manual Steps

1. **Node.js**: Download from https://nodejs.org (LTS version)
2. **Python**: Download from https://python.org (3.12+)
   - ✓ Add to PATH during installation
3. **Visual Studio Build Tools**: https://visualstudio.microsoft.com/downloads/
   - Select "Desktop development with C++"
4. **Git**: Download from https://git-scm.com/download/win

After installation:
```powershell
cd path\to\memory-line
npm install
```

### Ubuntu Manual Steps

```bash
# Update package list
sudo apt-get update

# Install Node.js 20.x LTS
curl -fsSL https://deb.nodesource.com/setup_20.x | sudo -E bash -
sudo apt-get install -y nodejs

# Install build tools and dependencies
sudo apt-get install -y build-essential python3 pkg-config libsqlite3-dev git

# Install npm dependencies
cd /path/to/memory-line
npm install
```

---

## Verifying Installation

After running the setup scripts, verify everything is installed correctly:

### Windows (PowerShell)

```powershell
# Check versions
node --version    # Should be v16+
npm --version     # Should be 7+
python --version  # Should be 3.12+
git --version     # Should show version

# Test build tools
npm install --dry-run
```

### Ubuntu (Bash)

```bash
# Check versions
node --version    # Should be v16+
npm --version     # Should be 7+
python3 --version # Should be 3.x
gcc --version     # Should show version
git --version     # Should show version

# Test build tools
npm install --dry-run
```

---

## Next Steps

After successful dependency installation:

1. **Configure API Keys** (optional for AI features):
   - Get Anthropic API key: https://console.anthropic.com/
   - Enter in Settings panel after first run

2. **Start Development**:
   ```bash
   npm run dev
   ```

3. **Run Tests**:
   ```bash
   npm test
   ```

4. **Build for Production**:
   ```bash
   npm run build
   npm run package
   ```

---

## Support

For issues or questions:
- Check the main [README.md](../README.md) in the project root
- Review error messages carefully
- Ensure you're running the latest version of the scripts
- Open an issue on GitHub with error details

---

**Last Updated**: 2025-11-24
**Compatible With**: Windows 11, Ubuntu 24.04 LTS (Electron/Node.js version)
