# Memory Timeline - Windows Native Application

A high-performance native Windows 11 implementation of Memory Timeline, featuring AI-powered event extraction, RAG-based cross-referencing, and deep Windows integration.

**Version:** 1.0.0-beta
**Platform:** Windows 11 (22H2+)
**Framework:** .NET 8 + WinUI 3
**Status:** 86% Complete (Phases 0-6 done, Phase 7 pending)

---

## Overview

This is the native Windows implementation of Memory Timeline, designed to provide superior performance and leverage Windows-specific features like NPU acceleration (future), Windows Ink, native touch/pen support, and deep OS integration.

### Key Advantages Over Electron Version

| Aspect | Electron | Windows Native |
|--------|----------|----------------|
| Memory (idle) | 150 MB | 30 MB |
| Memory (5000 events) | 300 MB | 100 MB |
| Cold start | 3-5 seconds | < 2 seconds |
| Timeline FPS | 30-45 FPS | 60 FPS |
| Package size | 120 MB | 30 MB |
| Touch/Pen | Limited | Full Windows Ink |
| Windows Integration | Basic | Full (JumpList, Timeline, Notifications) |

---

## Features

### Core Functionality
- **Timeline Visualization** - Interactive timeline with virtualization for 5000+ events at 60 FPS
- **Audio Recording** - Record audio memories with pause/resume using Windows MediaCapture
- **Speech-to-Text** - Transcribe recordings using Windows Speech Recognition
- **LLM Event Extraction** - Extract structured events from transcripts using Claude API
- **RAG Cross-References** - Discover connections between events using vector embeddings
- **Export/Import** - JSON, CSV, and Markdown export with JSON import support

### Windows 11 Integration
- **Toast Notifications** - Processing complete alerts with action buttons
- **JumpList** - Quick access to recent events and common actions
- **Windows Timeline** - Events appear in Windows Timeline with Adaptive Cards
- **Theme Support** - Light/Dark/System theme switching
- **Touch & Pen** - Full gesture support with Windows Ink ready

---

## Quick Start

### Prerequisites

1. **Windows 11** (Version 22H2 or later)
   - Check version: `winver` in Run dialog

2. **Visual Studio 2022** (Version 17.8+)
   - Download: https://visualstudio.microsoft.com/
   - Required workloads:
     - `.NET Desktop Development`
     - `Universal Windows Platform development`
     - `Windows App SDK C# Templates`

3. **.NET 8 SDK**
   - Included with Visual Studio 2022
   - Or download: https://dotnet.microsoft.com/download/dotnet/8.0

### Automated Setup

```powershell
# Navigate to scripts directory
cd windows-native\scripts

# Run setup (requires Administrator)
.\Setup-Dependencies.ps1 -Mode Development

# Verify installation
.\Verify-Installation.ps1 -Detailed
```

See [`scripts/README.md`](./scripts/README.md) for detailed script documentation.

### Build and Run

```powershell
# Clone and navigate
git clone <repository-url>
cd memory-line/windows-native/src

# Restore packages
dotnet restore

# Build
dotnet build

# Run (with Visual Studio)
# Press F5 to run with debugging
# Press Ctrl+F5 to run without debugging

# Run (command line)
cd MemoryTimeline
dotnet run
```

---

## Project Structure

```
windows-native/
├── src/
│   ├── MemoryTimeline/              # Main WinUI 3 application
│   │   ├── Views/                   # XAML pages
│   │   │   ├── TimelinePage.xaml
│   │   │   ├── QueuePage.xaml
│   │   │   ├── ReviewPage.xaml
│   │   │   ├── ConnectionsPage.xaml
│   │   │   └── SettingsPage.xaml
│   │   ├── ViewModels/              # MVVM view models
│   │   ├── Controls/                # Custom controls
│   │   ├── Assets/                  # Images, icons, resources
│   │   ├── App.xaml                 # Application entry point
│   │   ├── MainWindow.xaml          # Main window
│   │   └── Package.appxmanifest     # MSIX package configuration
│   │
│   ├── MemoryTimeline.Core/         # Business logic layer
│   │   ├── Services/                # Service interfaces & implementations
│   │   │   ├── IEventService.cs
│   │   │   ├── ISettingsService.cs
│   │   │   ├── IAudioService.cs
│   │   │   ├── ISpeechToTextService.cs
│   │   │   ├── ILlmService.cs
│   │   │   ├── IEmbeddingService.cs
│   │   │   ├── IRagService.cs
│   │   │   ├── IExportService.cs
│   │   │   ├── IImportService.cs
│   │   │   ├── INotificationService.cs
│   │   │   ├── IJumpListService.cs
│   │   │   └── IWindowsTimelineService.cs
│   │   └── Models/                  # Business models
│   │
│   ├── MemoryTimeline.Data/         # Data access layer
│   │   ├── Models/                  # EF Core entities
│   │   ├── Repositories/            # Repository pattern implementations
│   │   ├── Migrations/              # EF Core migrations
│   │   └── AppDbContext.cs          # EF Core database context
│   │
│   ├── MemoryTimeline.Tests/        # Unit & integration tests
│   │   ├── UnitTests/
│   │   ├── Integration/
│   │   └── Performance/
│   │
│   └── MemoryTimeline.sln           # Visual Studio solution file
│
├── scripts/                         # PowerShell automation scripts
│   ├── Setup-Dependencies.ps1
│   ├── Verify-Installation.ps1
│   └── README.md
│
├── packaging/                       # MSIX packaging configuration
│
├── .github/workflows/               # CI/CD pipelines
│
├── README.md                        # This file
├── DEVELOPMENT-STATUS.md            # Current development status
├── DEVELOPMENT-HISTORY.md           # Consolidated phase reports
├── TESTING.md                       # Testing guide
└── DEPLOYMENT.md                    # Deployment guide
```

---

## Architecture

### Clean Architecture Layers

```
┌─────────────────────────────────────────┐
│  Presentation Layer (WinUI 3 + XAML)    │
│  ┌──────────────────────────────────┐   │
│  │ Views (XAML)                     │   │
│  │ ViewModels (CommunityToolkit.Mvvm)│   │
│  │ Behaviors & Converters           │   │
│  └──────────────────────────────────┘   │
│              ↕                           │
│  ┌──────────────────────────────────┐   │
│  │ Application Layer                │   │
│  │ - Services (LLM, STT, RAG)       │   │
│  │ - Models                         │   │
│  │ - Business Logic                 │   │
│  └──────────────────────────────────┘   │
│              ↕                           │
│  ┌──────────────────────────────────┐   │
│  │ Data Access Layer                │   │
│  │ - EF Core + SQLite               │   │
│  │ - Repository Pattern             │   │
│  │ - Migrations                     │   │
│  └──────────────────────────────────┘   │
└─────────────────────────────────────────┘
```

### MVVM Pattern

- **Model**: Data entities in `MemoryTimeline.Data/Models/`
- **View**: XAML pages in `MemoryTimeline/Views/`
- **ViewModel**: View logic in `MemoryTimeline/ViewModels/`

Using `CommunityToolkit.Mvvm` for:
- `ObservableObject` base class
- `IRelayCommand` for commands
- `x:Bind` for compiled bindings

### Dependency Injection

Services registered in `App.xaml.cs`:

```csharp
services.AddDbContext<AppDbContext>();
services.AddSingleton<ISettingsService, SettingsService>();
services.AddScoped<IEventService, EventService>();
services.AddScoped<ILlmService, AnthropicService>();
services.AddScoped<IEmbeddingService, OpenAIEmbeddingService>();
services.AddScoped<IRagService, RagService>();
services.AddScoped<IExportService, ExportService>();
services.AddScoped<IImportService, ImportService>();
// ... more services
```

---

## Database

### SQLite with Entity Framework Core

**Database Location:**
```
%LOCALAPPDATA%\MemoryTimeline\memory-timeline.db
```

### Schema Overview

**Core Tables:**
- `events` - Timeline events
- `eras` - Life phases/periods
- `tags` - Event categorization
- `people` - People mentioned
- `locations` - Places

**Junction Tables:**
- `event_tags`, `event_people`, `event_locations`

**Processing Tables:**
- `recording_queue` - Audio files awaiting processing
- `pending_events` - Extracted events awaiting review

**RAG Tables:**
- `event_embeddings` - Vector embeddings
- `cross_references` - Event relationships

### Migrations

```powershell
cd MemoryTimeline.Data
dotnet ef migrations add MigrationName
dotnet ef database update
```

### Schema Compatibility

The Windows native app uses the **same database schema** as the Electron version, enabling easy migration.

---

## API Keys & Configuration

### Required API Keys

1. **Anthropic API Key** (for Claude LLM)
   - Get from: https://console.anthropic.com/

2. **OpenAI API Key** (for embeddings)
   - Get from: https://platform.openai.com/api-keys

### Configuration

API keys are stored securely in Windows Credential Manager. Configure via Settings page in the app.

**Settings Storage:**
- App settings: `app_settings` database table + Windows.Storage
- API keys: Windows Credential Manager

---

## Testing

### Run All Tests

```powershell
cd windows-native/src
dotnet test
```

### Run by Category

```powershell
# Unit tests
dotnet test --filter "FullyQualifiedName~UnitTests"

# Integration tests
dotnet test --filter "FullyQualifiedName~Integration"

# Performance tests
dotnet test --filter "FullyQualifiedName~Performance"
```

### Code Coverage

```powershell
dotnet test --collect:"XPlat Code Coverage"
```

See [`TESTING.md`](./TESTING.md) for comprehensive testing documentation.

---

## Deployment

### MSIX Package

Build MSIX for distribution:

**Visual Studio:**
1. Right-click `MemoryTimeline` project
2. Select `Publish > Create App Packages`
3. Follow wizard to create MSIX

**Command Line:**
```powershell
msbuild /t:Publish /p:Configuration=Release
```

### Microsoft Store

See [`DEPLOYMENT.md`](./DEPLOYMENT.md) for complete deployment instructions including:
- MSIX packaging
- Code signing
- Microsoft Store submission
- Side-loading for enterprise

---

## Performance Targets

| Metric | Target | Status |
|--------|--------|--------|
| Timeline FPS (5000 events) | 60 FPS | Implemented |
| Memory usage (5000 events) | < 100 MB | Implemented |
| Cold start time | < 2 seconds | Implemented |
| Database query time | < 50ms | Implemented |

---

## Development Status

**Current Phase:** Phase 7 (Testing & Deployment) - Pending

| Phase | Status |
|-------|--------|
| Phase 0: Preparation | ✅ Complete |
| Phase 1: Core Infrastructure | ✅ Complete |
| Phase 2: Timeline Visualization | ✅ Complete |
| Phase 3: Audio & Processing | ✅ Complete |
| Phase 4: LLM Integration | ✅ Complete |
| Phase 5: RAG & Embeddings | ✅ Complete |
| Phase 6: Polish & Integration | ✅ Complete |
| Phase 7: Testing & Deployment | ⬜ Pending |

See [`DEVELOPMENT-STATUS.md`](./DEVELOPMENT-STATUS.md) for detailed status.
See [`DEVELOPMENT-HISTORY.md`](./DEVELOPMENT-HISTORY.md) for consolidated phase reports.

---

## Troubleshooting

### Build Errors

**"Windows App SDK not found"**
- Install via NuGet: `dotnet add package Microsoft.WindowsAppSDK`
- Or update Visual Studio to 17.8+

**"Target framework not found"**
- Install .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0

**"SQLite DLL not found"**
- Try `dotnet restore --force-evaluate`

### Runtime Errors

**"Database file not found"**
- First run creates database automatically
- Check `%LOCALAPPDATA%\MemoryTimeline\` exists

**"MediaCapture not available"**
- Check microphone permissions in Windows Settings
- Ensure `Package.appxmanifest` has `microphone` capability

### Performance Issues

**Low FPS on timeline:**
- Enable hardware acceleration in GPU settings
- Check for GPU driver updates

**High memory usage:**
- Check for memory leaks with dotMemory
- Ensure proper disposal of large objects

---

## Documentation

| Document | Description |
|----------|-------------|
| [`DEVELOPMENT-STATUS.md`](./DEVELOPMENT-STATUS.md) | Current development status and next steps |
| [`DEVELOPMENT-HISTORY.md`](./DEVELOPMENT-HISTORY.md) | Consolidated phase completion reports |
| [`TESTING.md`](./TESTING.md) | Testing guide and best practices |
| [`DEPLOYMENT.md`](./DEPLOYMENT.md) | Deployment and distribution guide |
| [`scripts/README.md`](./scripts/README.md) | Setup scripts documentation |

---

## Contributing

See main repository `CONTRIBUTING.md` for guidelines.

### Windows-Specific Contributions

- Follow WinUI 3 design guidelines
- Use CommunityToolkit.Mvvm for MVVM
- Maintain feature parity with Electron version
- Add Windows-exclusive features when appropriate

---

## Resources

### Documentation
- [WinUI 3 Docs](https://learn.microsoft.com/windows/apps/winui/winui3/)
- [.NET 8 Docs](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-8)
- [EF Core Docs](https://learn.microsoft.com/ef/core/)
- [Windows App SDK](https://learn.microsoft.com/windows/apps/windows-app-sdk/)

### Tutorials
- [WinUI 3 Gallery](https://github.com/microsoft/WinUI-Gallery)
- [Windows ML](https://learn.microsoft.com/windows/ai/windows-ml/)

---

## License

MIT License - See main repository `LICENSE` file.

## Support

- **Issues:** GitHub Issues
- **Discussions:** GitHub Discussions

---

**Last Updated:** 2026-01-17
**Maintainer:** Development Team
