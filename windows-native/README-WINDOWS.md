# Memory Timeline - Windows Native Application

**Version:** 1.0.0-alpha
**Platform:** Windows 11 (22H2+)
**Framework:** .NET 8 + WinUI 3

This is the native Windows implementation of Memory Timeline, designed to provide superior performance and leverage Windows-specific features like NPU acceleration, Windows Ink, and native touch/pen support.

## Quick Setup

**For automated dependency installation**, use the PowerShell setup scripts:

```powershell
# Navigate to scripts directory
cd windows-native\scripts

# Run setup (requires Administrator)
.\Setup-Dependencies.ps1 -Mode Development

# Verify installation
.\Verify-Installation.ps1 -Detailed
```

See [`windows-native/scripts/README.md`](./scripts/README.md) for detailed script documentation.

## Prerequisites

### Required Software

1. **Windows 11** (Version 22H2 or later)
   - Check version: `winver` in Run dialog
   - For NPU features: Intel Core Ultra or AMD Ryzen AI processor

2. **Visual Studio 2022** (Version 17.8 or later)
   - Download: https://visualstudio.microsoft.com/
   - Required workloads:
     - `.NET Desktop Development`
     - `Universal Windows Platform development`
     - `Windows App SDK C# Templates`

3. **.NET 8 SDK**
   - Included with Visual Studio 2022
   - Or download: https://dotnet.microsoft.com/download/dotnet/8.0

4. **Windows App SDK 1.5+**
   - Installed via NuGet (referenced in project files)
   - Or manually: https://learn.microsoft.com/windows/apps/windows-app-sdk/

### Optional Tools

- **Windows Terminal** - Better development experience
- **Visual Studio Code** - For quick file edits
- **Git for Windows** - If not using Visual Studio's Git integration

## Getting Started

### 1. Clone the Repository

```powershell
git clone <repository-url>
cd memory-line
git checkout windows-native  # Switch to Windows native branch
```

### 2. Open the Solution

1. Navigate to `windows-native/src/`
2. Double-click `MemoryTimeline.sln` to open in Visual Studio 2022

Or from command line:
```powershell
cd windows-native/src
start MemoryTimeline.sln
```

### 3. Restore NuGet Packages

Visual Studio should automatically restore packages. If not:

```powershell
dotnet restore
```

Or in Visual Studio: `Tools > NuGet Package Manager > Restore NuGet Packages`

### 4. Build the Solution

**In Visual Studio:**
- Press `Ctrl+Shift+B` or
- Menu: `Build > Build Solution`

**Command Line:**
```powershell
dotnet build
```

### 5. Run the Application

**In Visual Studio:**
- Press `F5` to run with debugging
- Press `Ctrl+F5` to run without debugging

**Command Line:**
```powershell
cd MemoryTimeline
dotnet run
```

## Project Structure

```
windows-native/
├── src/
│   ├── MemoryTimeline/              # Main WinUI 3 application
│   │   ├── Views/                   # XAML pages
│   │   ├── ViewModels/              # MVVM view models
│   │   ├── Assets/                  # Images, icons, resources
│   │   ├── App.xaml                 # Application entry point
│   │   ├── MainWindow.xaml          # Main window
│   │   └── Package.appxmanifest     # MSIX package configuration
│   │
│   ├── MemoryTimeline.Core/         # Business logic layer
│   │   ├── Services/                # Service interfaces & implementations
│   │   └── Models/                  # Business models
│   │
│   ├── MemoryTimeline.Data/         # Data access layer
│   │   ├── Models/                  # EF Core entities
│   │   ├── Repositories/            # Repository pattern implementations
│   │   ├── Migrations/              # EF Core migrations
│   │   └── AppDbContext.cs          # EF Core database context
│   │
│   ├── MemoryTimeline.Tests/        # Unit & integration tests
│   │   ├── Services/                # Service tests
│   │   ├── Repositories/            # Repository tests
│   │   └── ViewModels/              # ViewModel tests
│   │
│   └── MemoryTimeline.sln           # Visual Studio solution file
│
├── .github/
│   └── workflows/                   # CI/CD pipelines
│
└── README-WINDOWS.md                # This file
```

## Architecture

### MVVM Pattern

This application follows the **Model-View-ViewModel (MVVM)** pattern:

- **Model**: Data entities in `MemoryTimeline.Data/Models/`
- **View**: XAML pages in `MemoryTimeline/Views/`
- **ViewModel**: View logic in `MemoryTimeline/ViewModels/`

### Clean Architecture

Organized in layers:

1. **Presentation** (MemoryTimeline) - WinUI 3 UI
2. **Application** (MemoryTimeline.Core) - Business logic
3. **Infrastructure** (MemoryTimeline.Data) - Data access

### Dependency Injection

Services are registered in `App.xaml.cs`:

```csharp
services.AddDbContext<AppDbContext>();
services.AddSingleton<ISettingsService, SettingsService>();
services.AddScoped<IEventService, EventService>();
// ... more services
```

## Database

### SQLite with Entity Framework Core

The app uses **SQLite** for local data storage, accessed via **EF Core 8**.

**Database Location:**
```
%LOCALAPPDATA%\MemoryTimeline\memory-timeline.db
```

### Migrations

To create a new migration:

```powershell
cd MemoryTimeline.Data
dotnet ef migrations add MigrationName
dotnet ef database update
```

### Schema Compatibility

The Windows native app uses the **same database schema** as the Electron version, enabling easy migration.

## Development Workflow

### Phase 0: Preparation ✅ COMPLETED

- [x] Project structure created
- [x] Solution and project files configured
- [x] Data models defined
- [x] Repository pattern implemented
- [x] Service interfaces created

### Phase 1: Core Infrastructure (CURRENT)

- [ ] Implement EventService
- [ ] Implement SettingsService
- [ ] Database migrations
- [ ] Complete App Shell UI
- [ ] Implement navigation

### Upcoming Phases

- **Phase 2**: Timeline Visualization (6 weeks)
- **Phase 3**: Audio Recording & Processing (4 weeks)
- **Phase 4**: LLM Integration (3 weeks)
- **Phase 5**: RAG & Embeddings (4 weeks)
- **Phase 6**: Polish & Windows Integration (3 weeks)
- **Phase 7**: Testing & Deployment (3 weeks)

See `MIGRATION-TO-NATIVE-WIN.md` for detailed roadmap.

## Testing

### Run All Tests

```powershell
dotnet test
```

### Run with Coverage

```powershell
dotnet test --collect:"XPlat Code Coverage"
```

### Run Specific Test Project

```powershell
dotnet test MemoryTimeline.Tests
```

## Windows-Specific Features

### NPU Acceleration

For AI inference on devices with NPU (Intel Core Ultra, AMD Ryzen AI):

- **DirectML backend** for ONNX Runtime
- **Whisper model** for local speech-to-text
- **Text embedding models** for semantic search

Fallback to CPU/GPU if NPU not available.

### Windows Ink

Support for pen input:

- Handwriting recognition
- Pressure sensitivity
- Palm rejection
- Digital ink annotations on timeline

### Touch Gestures

- Pinch to zoom
- Two-finger pan
- Swipe navigation
- Long-press context menu

### Windows 11 Integration

- Live Tiles / Widgets (dashboard summary)
- Jump Lists (recent timelines)
- Windows Timeline cards
- Toast notifications

## API Keys & Configuration

### Required API Keys

Store in Windows Credential Manager or app settings:

- **Anthropic API Key** (for Claude LLM)
- **OpenAI API Key** (optional, for cloud embeddings)

### Configuration

Settings stored in `app_settings` database table and `Windows.Storage`.

Edit via Settings page in the app or directly in database.

## Performance Targets

| Metric | Target | Current |
|--------|--------|---------|
| Timeline FPS (5000 events) | 60 FPS | TBD |
| Memory usage (5000 events) | < 100 MB | TBD |
| Cold start time | < 2 seconds | TBD |
| Database query time | < 50ms | TBD |

## Troubleshooting

### Build Errors

**Error: "Windows App SDK not found"**
- Install via NuGet: `dotnet add package Microsoft.WindowsAppSDK`
- Or update Visual Studio to 17.8+

**Error: "Target framework not found"**
- Install .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0

**Error: "SQLite DLL not found"**
- NuGet should auto-copy. Try `dotnet restore --force-evaluate`

### Runtime Errors

**Error: "Database file not found"**
- First run creates database automatically
- Check `%LOCALAPPDATA%\MemoryTimeline\` exists

**Error: "MediaCapture not available"**
- Check microphone permissions in Windows Settings
- Ensure `Package.appxmanifest` has `microphone` capability

### Performance Issues

**Low FPS on timeline:**
- Enable hardware acceleration in GPU settings
- Reduce number of visible events (virtualization)
- Check for GPU driver updates

**High memory usage:**
- Check for memory leaks with dotMemory
- Ensure proper disposal of large objects
- Use viewport-based object pooling

## Deployment

### MSIX Package

Build MSIX for distribution:

1. Right-click `MemoryTimeline` project
2. Select `Publish > Create App Packages`
3. Follow wizard to create MSIX

Or via command line:
```powershell
msbuild /t:Publish /p:Configuration=Release
```

### Code Signing

Requires EV Code Signing Certificate:

```powershell
signtool sign /fd SHA256 /f certificate.pfx /p password MemoryTimeline.msix
```

### Microsoft Store

1. Create Partner Center account
2. Upload MSIX package
3. Fill store listing details
4. Submit for certification

## Contributing

See main repository `CONTRIBUTING.md` for guidelines.

### Windows-Specific Contributions

- Follow WinUI 3 design guidelines
- Use CommunityToolkit.Mvvm for MVVM
- Maintain feature parity with Electron version
- Add Windows-exclusive features when appropriate

## Resources

### Documentation

- [WinUI 3 Docs](https://learn.microsoft.com/windows/apps/winui/winui3/)
- [.NET 8 Docs](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-8)
- [EF Core Docs](https://learn.microsoft.com/ef/core/)
- [Windows App SDK](https://learn.microsoft.com/windows/apps/windows-app-sdk/)

### Tutorials

- [WinUI 3 Gallery](https://github.com/microsoft/WinUI-Gallery)
- [.NET MAUI Samples](https://github.com/dotnet/maui-samples)
- [Windows ML](https://learn.microsoft.com/windows/ai/windows-ml/)

## License

MIT License - See main repository `LICENSE` file.

## Support

- **Issues**: GitHub Issues
- **Discussions**: GitHub Discussions
- **Email**: [Support email]

---

**Last Updated:** 2025-11-24
**Maintainer:** Development Team
**Status:** Alpha - Active Development
