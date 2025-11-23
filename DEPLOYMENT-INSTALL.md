# Memory Timeline - Deployment & Installation Guide

> **Version:** 1.0.0
> **Last Updated:** 2025-11-21
> **Target Platforms:** Windows 11, macOS 10.15+, Linux (Ubuntu 20.04+)

## Table of Contents

1. [Overview](#overview)
2. [Prerequisites](#prerequisites)
3. [Development Setup](#development-setup)
4. [Building for Production](#building-for-production)
5. [Creating Installers](#creating-installers)
6. [Deployment](#deployment)
7. [User Installation](#user-installation)
8. [Configuration](#configuration)
9. [Troubleshooting](#troubleshooting)
10. [Updating](#updating)

---

## Overview

Memory Timeline is an Electron-based desktop application for creating personal memory timelines with AI-powered event extraction from audio recordings. This guide covers development setup, building, packaging, and deployment procedures.

### Application Architecture

- **Frontend:** React 18 with Hooks, Zustand state management
- **Backend:** Electron main process with Node.js
- **Database:** SQLite (better-sqlite3) with full-text search
- **AI/LLM:** Anthropic Claude API for event extraction
- **STT:** Multiple speech-to-text engine support
- **Build System:** Webpack + Babel, Electron Forge for packaging

---

## Prerequisites

### Development Requirements

| Tool | Version | Purpose |
|------|---------|---------|
| **Node.js** | 16.x or higher | JavaScript runtime |
| **npm** | 8.x or higher | Package manager |
| **Git** | 2.x or higher | Version control |
| **Python** | 3.8+ (optional) | Native module compilation |
| **Visual Studio Build Tools** | Latest (Windows only) | Native module compilation |
| **Xcode Command Line Tools** | Latest (macOS only) | Native module compilation |

### System Requirements

**Minimum:**
- **OS:** Windows 10 (64-bit), macOS 10.15+, Ubuntu 20.04+
- **RAM:** 4 GB
- **Storage:** 500 MB free space
- **CPU:** 64-bit processor

**Recommended:**
- **RAM:** 8 GB or more
- **Storage:** 1 GB free space
- **CPU:** Multi-core processor for better performance

---

## Development Setup

### 1. Clone the Repository

```bash
git clone https://github.com/ethanbissbort/memory-timeline.git
cd memory-timeline
```

### 2. Install Dependencies

#### Standard Installation

```bash
npm install
```

#### Installation in Restricted Environments

If you encounter Electron download issues:

```bash
# Use a local Electron mirror
npm config set electron_mirror https://npmmirror.com/mirrors/electron/
npm install

# Or skip Electron download during install
npm install --ignore-scripts

# Then download Electron separately
npx electron install
```

### 3. Configure Environment Variables (Optional)

Create a `.env` file in the project root:

```env
# Development mode
NODE_ENV=development

# API Keys (optional - can be set in app Settings)
ANTHROPIC_API_KEY=your_api_key_here
OPENAI_API_KEY=your_openai_key_here

# Electron settings
ELECTRON_ENABLE_LOGGING=true
```

### 4. Run Development Server

```bash
# Start webpack dev server and Electron concurrently
npm run dev

# Or run separately
npm run dev:renderer  # Start webpack dev server (port 8080)
npm run dev:electron  # Start Electron in development mode
```

The application will open automatically with hot-reload enabled.

### 5. Run Linting and Tests

```bash
# Lint code
npm run lint

# Run tests (when implemented)
npm test
```

---

## Building for Production

### 1. Build Renderer Process

```bash
npm run build
```

This compiles the React application using Webpack and outputs to `dist/`.

**Build Output:**
- `dist/index.html` - Main HTML file
- `dist/bundle.js` - Compiled JavaScript
- `dist/styles.css` - Compiled styles

### 2. Test Production Build

```bash
# Set production mode and start Electron
cross-env NODE_ENV=production electron .
```

### 3. Verify Build

Check that:
- [ ] Application starts without errors
- [ ] Database initializes correctly
- [ ] All panels load (Timeline, Recorder, Queue, Settings)
- [ ] Audio recording works
- [ ] LLM integration functions (with API key)
- [ ] Export/import features work

---

## Creating Installers

Memory Timeline uses **Electron Forge** to create platform-specific installers.

### Platform-Specific Installers

#### Windows (Squirrel Installer)

```bash
# Package for Windows
npm run package -- --platform win32

# Create Windows installer
npm run make -- --platform win32
```

**Output:** `out/make/squirrel.windows/x64/MemoryTimelineSetup.exe`

**Installer Features:**
- Auto-updates via Squirrel
- Start menu shortcuts
- Uninstaller
- Custom icon

#### macOS (DMG)

```bash
# Package for macOS
npm run package -- --platform darwin

# Create macOS installer
npm run make -- --platform darwin
```

**Output:** `out/make/Memory Timeline-1.0.0.dmg`

**Requirements for Distribution:**
- Apple Developer ID certificate for code signing
- Notarization for Catalina+ (see [Code Signing](#code-signing))

#### Linux (DEB and RPM)

```bash
# Create Debian package
npm run make -- --platform linux --arch x64

# Create RPM package (if configured)
npm run make -- --platform linux
```

**Output:**
- `out/make/deb/x64/memory-timeline_1.0.0_amd64.deb`
- `out/make/rpm/x64/memory-timeline-1.0.0.x86_64.rpm`

#### Create All Platforms

```bash
npm run make
```

This creates installers for all configured platforms. **Note:** You can only build for platforms your current OS supports (e.g., macOS DMG requires macOS).

---

## Code Signing

### Windows Code Signing

**Requirements:**
- Code signing certificate (.pfx file)
- Certificate password

**Setup:**

1. Obtain a code signing certificate from a trusted CA (e.g., DigiCert, Sectigo)

2. Set environment variables:

```bash
# Windows
set WINDOWS_CERTIFICATE_FILE=path\to\certificate.pfx
set WINDOWS_CERTIFICATE_PASSWORD=your_password

# macOS/Linux
export WINDOWS_CERTIFICATE_FILE=path/to/certificate.pfx
export WINDOWS_CERTIFICATE_PASSWORD=your_password
```

3. Update `forge.config.js`:

```javascript
certificateFile: process.env.WINDOWS_CERTIFICATE_FILE,
certificatePassword: process.env.WINDOWS_CERTIFICATE_PASSWORD
```

### macOS Code Signing & Notarization

**Requirements:**
- Apple Developer account ($99/year)
- Valid Developer ID Application certificate
- App-specific password for notarization

**Setup:**

1. Install certificates from Apple Developer portal

2. Configure signing in `forge.config.js`:

```javascript
osxSign: {
    identity: 'Developer ID Application: Your Name (XXXXXXXXXX)',
    'hardened-runtime': true,
    entitlements: 'entitlements.plist',
    'entitlements-inherit': 'entitlements.plist',
    'signature-flags': 'library'
}
```

3. Create `entitlements.plist`:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>com.apple.security.cs.allow-jit</key>
    <true/>
    <key>com.apple.security.cs.allow-unsigned-executable-memory</key>
    <true/>
    <key>com.apple.security.cs.allow-dyld-environment-variables</key>
    <true/>
</dict>
</plist>
```

4. Configure notarization:

```javascript
osxNotarize: {
    appleId: process.env.APPLE_ID,
    appleIdPassword: process.env.APPLE_ID_PASSWORD,
    teamId: process.env.APPLE_TEAM_ID
}
```

---

## Deployment

### Distribution Methods

#### 1. Direct Download (GitHub Releases)

**Steps:**

1. Create a new release on GitHub:

```bash
git tag -a v1.0.0 -m "Release version 1.0.0"
git push origin v1.0.0
```

2. Upload installers to GitHub Release assets:
   - `MemoryTimelineSetup.exe` (Windows)
   - `Memory Timeline-1.0.0.dmg` (macOS)
   - `memory-timeline_1.0.0_amd64.deb` (Linux)
   - `memory-timeline-1.0.0.x86_64.rpm` (Linux)

3. Write release notes with:
   - New features
   - Bug fixes
   - Breaking changes
   - Installation instructions

#### 2. Auto-Updates (Squirrel for Windows)

**Setup Squirrel Update Server:**

1. Host a static file server with update files

2. Configure update feed URL in `src/main/main.js`:

```javascript
const { autoUpdater } = require('electron-updater');

autoUpdater.setFeedURL({
    provider: 'generic',
    url: 'https://your-domain.com/updates/'
});

autoUpdater.checkForUpdatesAndNotify();
```

3. Upload release files to update server:
   - `RELEASES` file (generated by Squirrel)
   - `.nupkg` files (delta updates)

#### 3. Microsoft Store (Windows)

1. Convert to MSIX package:

```bash
npm install @electron/windows-store --save-dev
npx electron-windows-store --input-directory ./out/Memory\ Timeline-win32-x64 --output-directory ./out/store --package-name MemoryTimeline
```

2. Submit to Microsoft Partner Center

#### 4. Mac App Store

1. Configure entitlements and provisioning profiles
2. Package with App Store build config
3. Submit via Xcode or Transporter

---

## User Installation

### Windows

**Method 1: Installer (.exe)**

1. Download `MemoryTimelineSetup.exe`
2. Run installer
3. Follow on-screen instructions
4. Launch from Start Menu or Desktop shortcut

**Method 2: Portable (ZIP)**

1. Download `Memory Timeline-win32-x64.zip`
2. Extract to desired location
3. Run `memory-timeline.exe`

### macOS

**Method 1: DMG**

1. Download `Memory Timeline-1.0.0.dmg`
2. Open DMG file
3. Drag `Memory Timeline.app` to Applications folder
4. Right-click and select "Open" (first time only)

**Method 2: Homebrew Cask (if published)**

```bash
brew install --cask memory-timeline
```

### Linux

**Debian/Ubuntu (.deb)**

```bash
sudo dpkg -i memory-timeline_1.0.0_amd64.deb
sudo apt-get install -f  # Install dependencies
```

**Fedora/RHEL (.rpm)**

```bash
sudo rpm -i memory-timeline-1.0.0.x86_64.rpm
```

**AppImage (Universal)**

```bash
chmod +x Memory-Timeline-1.0.0.AppImage
./Memory-Timeline-1.0.0.AppImage
```

---

## Configuration

### First-Time Setup

When launching the application for the first time:

1. **Navigate to Settings** (⚙️ icon in sidebar)

2. **Configure LLM API Key** (required for event extraction):
   - Click "API Configuration"
   - Enter your Anthropic API key
   - Get key from: https://console.anthropic.com/

3. **Configure Speech-to-Text Engine** (optional):
   - Select STT engine (Mock, Whisper Local, Whisper API, etc.)
   - Enter engine-specific configuration (API key or model path)
   - Click "Initialize STT Engine"

4. **Configure RAG Settings** (optional):
   - Select embedding provider (OpenAI, Voyage AI, Cohere, Local)
   - Choose embedding model
   - Enter API key (if cloud provider)
   - Click "Initialize Embedding Service"

### Data Location

| Platform | Path |
|----------|------|
| **Windows** | `%APPDATA%\memory-timeline\` |
| **macOS** | `~/Library/Application Support/memory-timeline/` |
| **Linux** | `~/.config/memory-timeline/` |

**Files:**
- `timeline.db` - SQLite database
- `assets/audio/` - Recorded audio files
- `logs/` - Application logs

### Backup & Restore

**Manual Backup:**

1. Go to Settings → Database Management
2. Click "Create Backup"
3. Choose backup location
4. Save `memory-timeline-backup-YYYY-MM-DD.db`

**Restore from Backup:**

1. Close the application
2. Replace `timeline.db` with backup file
3. Restart the application

**Automated Backup Script (Optional):**

```bash
# Linux/macOS
#!/bin/bash
cp ~/.config/memory-timeline/timeline.db ~/backups/timeline-$(date +%Y%m%d).db

# Windows (PowerShell)
Copy-Item "$env:APPDATA\memory-timeline\timeline.db" `
  -Destination "$env:USERPROFILE\backups\timeline-$(Get-Date -Format yyyyMMdd).db"
```

---

## Troubleshooting

### Common Issues

#### 1. Application Won't Start

**Symptoms:** App crashes on launch or shows error dialog

**Solutions:**

- Check system requirements
- Update to latest version
- Delete database and restart (will lose data):
  ```bash
  # Windows
  del "%APPDATA%\memory-timeline\timeline.db"

  # macOS/Linux
  rm ~/.config/memory-timeline/timeline.db
  ```

#### 2. Database Corruption

**Symptoms:** "Database disk image is malformed" error

**Solutions:**

1. Try database recovery:
   ```bash
   sqlite3 timeline.db ".recover" | sqlite3 timeline_recovered.db
   ```

2. Restore from backup (Settings → Database Management → Restore)

3. Use SQLite CLI to repair:
   ```bash
   sqlite3 timeline.db "PRAGMA integrity_check;"
   sqlite3 timeline.db "VACUUM;"
   ```

#### 3. Audio Recording Not Working

**Symptoms:** Microphone access denied or recording fails

**Solutions:**

- **Windows:** Settings → Privacy → Microphone → Allow apps
- **macOS:** System Preferences → Security & Privacy → Microphone
- **Linux:** Check PulseAudio/ALSA configuration

#### 4. LLM API Errors

**Symptoms:** "API key not set" or "Failed to extract event data"

**Solutions:**

- Verify API key in Settings
- Check API key has correct permissions
- Verify internet connection
- Check Anthropic API status: https://status.anthropic.com/

#### 5. Performance Issues with Large Databases

**Symptoms:** Slow loading, lag when scrolling timeline

**Solutions:**

1. Optimize database:
   - Settings → Database Management → "Optimize Database"

2. Clear performance cache:
   - Restart application
   - Or use developer console: `window.electronAPI.performance.clearCache()`

3. Reduce timeline view range:
   - Use zoom controls to view smaller date ranges
   - Filter by category or era

#### 6. Export Failures

**Symptoms:** Export completes but file is empty or corrupted

**Solutions:**

- Ensure write permissions for target directory
- Check available disk space
- Try exporting to different location
- Verify database integrity first

---

## Updating

### Automatic Updates (Windows with Squirrel)

If auto-updates are configured:

1. App checks for updates on startup
2. Notification appears when update available
3. Click "Install Update"
4. App restarts with new version

### Manual Updates

#### Windows

1. Download new installer
2. Run installer (will update existing installation)
3. No need to uninstall old version

#### macOS

1. Download new DMG
2. Replace app in Applications folder
3. First launch may require "Open" from right-click menu

#### Linux

```bash
# Debian/Ubuntu
sudo dpkg -i memory-timeline_1.1.0_amd64.deb

# Fedora/RHEL
sudo rpm -Uvh memory-timeline-1.1.0.x86_64.rpm
```

### Migration Notes

**Version 1.x to 2.x:**
- Backup database before upgrading
- Database schema will auto-migrate
- Check release notes for breaking changes

---

## Advanced Configuration

### Custom Electron Flags

Launch with custom flags:

```bash
# Disable GPU acceleration (for compatibility)
memory-timeline --disable-gpu

# Enable verbose logging
memory-timeline --enable-logging --v=1

# Specific data directory
memory-timeline --user-data-dir=/custom/path
```

### Environment Variables

```bash
# Windows
set ELECTRON_ENABLE_LOGGING=true
set DEBUG=*

# macOS/Linux
export ELECTRON_ENABLE_LOGGING=true
export DEBUG=*
```

### Developer Tools

Enable developer console:

- **Windows/Linux:** `Ctrl + Shift + I`
- **macOS:** `Cmd + Option + I`

Or add to code:

```javascript
mainWindow.webContents.openDevTools();
```

---

## Performance Tuning

### For Large Databases (1000+ Events)

1. **Enable database optimizations** (automatic in v1.0+)
2. **Use pagination** when viewing events
3. **Limit embedding generation** to important events
4. **Regular maintenance:**
   ```bash
   # Vacuum database monthly
   sqlite3 timeline.db "VACUUM;"

   # Analyze for query optimization
   sqlite3 timeline.db "ANALYZE;"
   ```

### For Low-Memory Systems

1. Reduce zoom range on timeline
2. Disable auto-embedding generation
3. Close other applications
4. Use local STT instead of cloud APIs

---

## Security Best Practices

1. **API Keys:** Store securely, never commit to version control
2. **Database:** Regular backups to encrypted storage
3. **Updates:** Keep application and dependencies updated
4. **Permissions:** Only grant necessary system permissions
5. **Network:** Use HTTPS for all API calls (configured by default)

---

## Support & Resources

- **Documentation:** https://github.com/yourusername/memory-timeline/wiki
- **Issues:** https://github.com/yourusername/memory-timeline/issues
- **Discussions:** https://github.com/yourusername/memory-timeline/discussions
- **Email:** support@memorytimeline.app

---

## License

MIT License - See LICENSE file for details.

---

**Document Version:** 1.0.0
**Last Updated:** 2025-11-21
**Maintained By:** Memory Timeline Development Team
