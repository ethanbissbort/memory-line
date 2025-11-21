# Memory Timeline - Deployment Guide

This guide covers the complete deployment process for the Memory Timeline Windows native application.

## Table of Contents
1. [Prerequisites](#prerequisites)
2. [Building the Application](#building-the-application)
3. [Running Tests](#running-tests)
4. [MSIX Packaging](#msix-packaging)
5. [Code Signing](#code-signing)
6. [Microsoft Store Deployment](#microsoft-store-deployment)
7. [Side-loading](#side-loading)
8. [CI/CD Pipeline](#cicd-pipeline)

---

## Prerequisites

### Development Environment
- **Windows 11 22H2** or later (required for Windows App SDK 1.5+)
- **Visual Studio 2022** (17.8 or later) with workloads:
  - .NET Desktop Development
  - Universal Windows Platform development
  - Windows App SDK C# Templates
- **.NET 8 SDK** (8.0 or later)
- **Windows App SDK** 1.5 or later

### For Deployment
- **Windows SDK** (10.0.22621.0 or later)
- **MSIX Packaging Tool** (from Microsoft Store)
- **Code Signing Certificate** (EV certificate for Microsoft Store)
- **Microsoft Partner Center account** (for Store deployment)

---

## Building the Application

### Debug Build
```powershell
# Navigate to solution directory
cd windows-native/src

# Restore dependencies
dotnet restore

# Build in Debug mode
dotnet build --configuration Debug
```

### Release Build
```powershell
# Build in Release mode with optimizations
dotnet build --configuration Release

# Or using MSBuild
msbuild MemoryTimeline.sln /p:Configuration=Release /p:Platform=x64
```

### Build Output
The compiled application will be in:
```
windows-native/src/MemoryTimeline/bin/Release/net8.0-windows10.0.22621.0/
```

---

## Running Tests

### All Tests
```powershell
cd windows-native/src
dotnet test --configuration Release
```

### Unit Tests Only
```powershell
dotnet test --filter "FullyQualifiedName~UnitTests"
```

### Integration Tests
```powershell
dotnet test --filter "FullyQualifiedName~Integration"
```

### Performance Tests
```powershell
dotnet test --filter "FullyQualifiedName~Performance" --logger "console;verbosity=detailed"
```

### Code Coverage
```powershell
# Install coverage tool
dotnet tool install --global dotnet-coverage

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Generate HTML report
dotnet tool install --global dotnet-reportgenerator-globaltool
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:./coverage-report -reporttypes:Html
```

### Coverage Requirements
- Target: **> 80% code coverage**
- Critical paths (event CRUD, audio processing, LLM integration) should have **> 90% coverage**

---

## MSIX Packaging

### Manual Packaging with Visual Studio

1. **Add Packaging Project**
   ```
   File → Add → New Project → Windows Application Packaging Project
   ```

2. **Configure Package Manifest**
   - Use the provided `packaging/Package.appxmanifest`
   - Update Identity Publisher with your certificate CN
   - Set appropriate version number

3. **Add Application Reference**
   - Right-click packaging project → Add → Reference
   - Select MemoryTimeline project

4. **Create Package**
   ```
   Right-click packaging project → Publish → Create App Packages
   ```

### Command-Line Packaging

```powershell
# Set environment variables
$AppxManifestPath = ".\packaging\Package.appxmanifest"
$OutputPath = ".\output\packages"
$MappingFile = ".\packaging\FileMapping.txt"

# Create package using makeappx
makeappx pack /d ".\bin\Release\net8.0-windows\" /p "$OutputPath\MemoryTimeline.msix" /l

# For app bundle (multiple architectures)
makeappx bundle /d "$OutputPath\bundles" /p "$OutputPath\MemoryTimeline.msixbundle"
```

### Package Contents Verification
```powershell
# List package contents
makeappx unpack /p MemoryTimeline.msix /d .\unpacked /l

# Validate package
certutil -hashfile MemoryTimeline.msix SHA256
```

---

## Code Signing

### Requirements
- **EV Code Signing Certificate** (required for Microsoft Store)
- Valid timestamp server URL
- SignTool.exe (included with Windows SDK)

### Signing Command
```powershell
# Set certificate thumbprint
$CertThumbprint = "YOUR_CERT_THUMBPRINT"

# Sign the package
signtool sign /fd SHA256 /sha1 $CertThumbprint /t http://timestamp.digicert.com /v MemoryTimeline.msix

# Verify signature
signtool verify /pa MemoryTimeline.msix
```

### Certificate Requirements
- **Type**: EV (Extended Validation) Code Signing Certificate
- **Algorithm**: SHA256 or higher
- **Validity**: Must be valid at time of signing
- **Trusted Root**: Must chain to a trusted root authority

### Acquiring a Certificate
1. Purchase from authorized CA (DigiCert, Sectigo, GlobalSign)
2. Complete identity verification process
3. Install on signing machine (hardware token or HSM)
4. Export thumbprint for automation

---

## Microsoft Store Deployment

### 1. Partner Center Setup

1. **Create Developer Account**
   - Go to [Microsoft Partner Center](https://partner.microsoft.com)
   - Register as individual or company
   - Pay registration fee ($19 one-time for individual, $99 for company)

2. **Reserve App Name**
   - Apps → Create a new app
   - Reserve "Memory Timeline"
   - Note the Identity details for manifest

### 2. App Submission Preparation

#### Required Assets
```
Assets/
├── StoreLogo.png (50x50)
├── Square44x44Logo.png (44x44)
├── Square150x150Logo.png (150x150)
├── Square310x310Logo.png (310x310)
├── Wide310x150Logo.png (310x150)
├── SmallTile.png (71x71)
├── SplashScreen.png (620x300)
└── Screenshots/
    ├── Desktop_1920x1080_1.png
    ├── Desktop_1920x1080_2.png
    ├── Desktop_1920x1080_3.png
    └── Desktop_1920x1080_4.png
```

#### Store Listing Information
- **Description** (minimum 200 characters)
- **Features** (5-10 bullet points)
- **Screenshots** (minimum 1, recommended 4+)
- **Privacy Policy URL** (required)
- **Support Contact** (email or website)
- **Age Rating** (ESRB, PEGI, etc.)
- **Category** (Productivity)

### 3. Package Upload

1. **Create Submission**
   ```
   Partner Center → Your App → Start submission
   ```

2. **Upload Package**
   - Upload signed `.msix` or `.msixbundle`
   - System will validate:
     - Digital signature
     - Manifest validity
     - API usage compliance
     - Content policy compliance

3. **Configure Properties**
   - Display name: "Memory Timeline"
   - Category: Productivity
   - Pricing: Free (or set price)
   - Markets: Select target countries
   - Age rating: Complete questionnaire

4. **Submit for Certification**
   - Review all sections
   - Submit for review
   - Typical review time: 1-3 business days

### 4. Certification Process

Microsoft will test:
- **Security**: Malware scan, signature validation
- **Performance**: Launch time, memory usage, CPU usage
- **Compatibility**: Windows version compatibility
- **Content**: Policy compliance
- **Functionality**: Basic app functionality

### 5. Publishing

Once approved:
- App goes live in Microsoft Store (usually within 24 hours)
- Users can download and install
- Auto-updates handled by Microsoft Store

---

## Side-loading

For enterprise deployment or testing without Microsoft Store.

### 1. Enable Side-loading on Target Machine

```powershell
# Check if side-loading is enabled
Get-AppxPackage -Name "*YourPublisher*"

# Enable Developer Mode (Settings → Update & Security → For developers)
# Or use PowerShell (requires admin)
reg add "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" /t REG_DWORD /f /v "AllowDevelopmentWithoutDevLicense" /d "1"
```

### 2. Install Certificate

```powershell
# Import signing certificate to trusted root (admin required)
$cert = Get-PfxCertificate -FilePath ".\SigningCert.pfx"
Import-Certificate -CertStoreLocation Cert:\LocalMachine\Root -Certificate $cert
```

### 3. Install Package

```powershell
# Using Add-AppxPackage
Add-AppxPackage -Path ".\MemoryTimeline.msix"

# With dependencies
Add-AppxPackage -Path ".\MemoryTimeline.msix" -DependencyPath ".\Dependencies\*.appx"

# Verify installation
Get-AppxPackage -Name "MemoryTimeline"
```

### 4. PowerShell Install Script

Create `Install.ps1`:

```powershell
#Requires -RunAsAdministrator

param(
    [string]$PackagePath = ".\MemoryTimeline.msix",
    [string]$CertificatePath = ".\SigningCert.cer"
)

Write-Host "Installing Memory Timeline..." -ForegroundColor Green

# Check if package exists
if (-not (Test-Path $PackagePath)) {
    Write-Error "Package not found: $PackagePath"
    exit 1
}

# Install certificate
if (Test-Path $CertificatePath) {
    Write-Host "Installing certificate..."
    Import-Certificate -CertStoreLocation Cert:\LocalMachine\Root -FilePath $CertificatePath
}

# Install package
Write-Host "Installing package..."
try {
    Add-AppxPackage -Path $PackagePath -ErrorAction Stop
    Write-Host "Installation successful!" -ForegroundColor Green
    Write-Host "You can now launch Memory Timeline from the Start menu."
}
catch {
    Write-Error "Installation failed: $_"
    exit 1
}
```

### 5. Uninstall Script

Create `Uninstall.ps1`:

```powershell
#Requires -RunAsAdministrator

$AppName = "MemoryTimeline"

Write-Host "Uninstalling $AppName..." -ForegroundColor Yellow

$package = Get-AppxPackage -Name "*$AppName*"

if ($package) {
    Remove-AppxPackage -Package $package.PackageFullName
    Write-Host "Uninstallation successful!" -ForegroundColor Green
}
else {
    Write-Host "$AppName is not installed." -ForegroundColor Yellow
}
```

---

## CI/CD Pipeline

### GitHub Actions Example

Create `.github/workflows/build-and-deploy.yml`:

```yaml
name: Build and Deploy

on:
  push:
    branches: [ main ]
  pull_request:
    branches: [ main ]
  release:
    types: [ published ]

jobs:
  build-and-test:
    runs-on: windows-latest

    steps:
    - name: Checkout code
      uses: actions/checkout@v3

    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '8.0.x'

    - name: Restore dependencies
      run: dotnet restore windows-native/src/MemoryTimeline.sln

    - name: Build
      run: dotnet build windows-native/src/MemoryTimeline.sln --configuration Release --no-restore

    - name: Run Tests
      run: dotnet test windows-native/src/MemoryTimeline.sln --configuration Release --no-build --verbosity normal --collect:"XPlat Code Coverage"

    - name: Upload Coverage
      uses: codecov/codecov-action@v3
      with:
        files: '**/coverage.cobertura.xml'

    - name: Create MSIX Package
      run: |
        # Package creation commands here
        makeappx pack /d windows-native/src/MemoryTimeline/bin/Release/net8.0-windows/ /p MemoryTimeline.msix

    - name: Sign Package
      if: github.event_name == 'release'
      run: |
        # Signing commands here
        # Note: Certificate should be stored in GitHub Secrets
        signtool sign /fd SHA256 /sha1 ${{ secrets.CERT_THUMBPRINT }} /t http://timestamp.digicert.com MemoryTimeline.msix

    - name: Upload Artifact
      uses: actions/upload-artifact@v3
      with:
        name: msix-package
        path: MemoryTimeline.msix

  deploy-to-store:
    needs: build-and-test
    if: github.event_name == 'release'
    runs-on: windows-latest

    steps:
    - name: Download Artifact
      uses: actions/download-artifact@v3
      with:
        name: msix-package

    - name: Upload to Microsoft Store
      uses: isaacrlevin/windows-store-action@v1
      with:
        tenant-id: ${{ secrets.TENANT_ID }}
        client-id: ${{ secrets.CLIENT_ID }}
        client-secret: ${{ secrets.CLIENT_SECRET }}
        app-id: ${{ secrets.STORE_APP_ID }}
        package-path: MemoryTimeline.msix
```

### Azure DevOps Pipeline Example

Create `azure-pipelines.yml`:

```yaml
trigger:
- main

pool:
  vmImage: 'windows-latest'

variables:
  solution: 'windows-native/src/MemoryTimeline.sln'
  buildConfiguration: 'Release'
  buildPlatform: 'x64'

steps:
- task: NuGetToolInstaller@1

- task: NuGetCommand@2
  inputs:
    restoreSolution: '$(solution)'

- task: VSBuild@1
  inputs:
    solution: '$(solution)'
    platform: '$(buildPlatform)'
    configuration: '$(buildConfiguration)'

- task: VSTest@2
  inputs:
    platform: '$(buildPlatform)'
    configuration: '$(buildConfiguration)'
    codeCoverageEnabled: true

- task: PowerShell@2
  displayName: 'Create MSIX Package'
  inputs:
    targetType: 'inline'
    script: |
      makeappx pack /d $(Build.SourcesDirectory)/windows-native/src/MemoryTimeline/bin/Release/net8.0-windows/ /p $(Build.ArtifactStagingDirectory)/MemoryTimeline.msix

- task: PublishBuildArtifacts@1
  inputs:
    PathtoPublish: '$(Build.ArtifactStagingDirectory)'
    ArtifactName: 'drop'
```

---

## Post-Deployment

### Monitoring
- Monitor Partner Center for crash reports
- Review user feedback and ratings
- Track download statistics
- Monitor API usage and costs (Anthropic, OpenAI)

### Updates
1. Increment version in `Package.appxmanifest`
2. Build and test new version
3. Create new submission in Partner Center
4. Upload new package
5. Microsoft Store handles auto-updates for users

### Rollback
If critical issues found:
1. Suspend availability in Partner Center
2. Roll back to previous version
3. Fix issues and resubmit

---

## Troubleshooting

### Common Issues

**Certificate Not Trusted**
```powershell
# Solution: Install certificate to trusted root
Import-Certificate -CertStoreLocation Cert:\LocalMachine\Root -FilePath SigningCert.cer
```

**Package Installation Failed**
```powershell
# Check logs
Get-AppxLog -ActivityId <ID>

# Common fixes:
# 1. Ensure certificate is trusted
# 2. Check Windows version compatibility
# 3. Verify package integrity
```

**Store Submission Rejected**
- Review certification report in Partner Center
- Common reasons:
  - Invalid manifest
  - Missing privacy policy
  - Content policy violations
  - Performance issues
  - Incomplete metadata

---

## Resources

- [Windows App SDK Documentation](https://docs.microsoft.com/windows/apps/windows-app-sdk/)
- [MSIX Packaging Documentation](https://docs.microsoft.com/windows/msix/)
- [Microsoft Partner Center](https://partner.microsoft.com/dashboard)
- [Code Signing Best Practices](https://docs.microsoft.com/windows/win32/seccrypto/cryptography-tools)

---

## Support

For deployment issues:
- Email: support@memorytimeline.com
- GitHub Issues: https://github.com/yourusername/memory-timeline/issues
- Documentation: https://docs.memorytimeline.com

---

**Last Updated**: 2024-11-21
**Version**: 1.0.0
