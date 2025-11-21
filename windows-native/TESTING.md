# Memory Timeline - Testing Guide

This document provides comprehensive information about the testing strategy and how to run tests for the Memory Timeline Windows native application.

## Test Structure

The test suite is organized into three main categories:

```
MemoryTimeline.Tests/
├── Services/              # Legacy basic tests
├── UnitTests/            # Comprehensive unit tests
│   ├── QueueServiceTests.cs
│   ├── ExportServiceTests.cs
│   ├── ImportServiceTests.cs
│   └── RagServiceTests.cs
├── Integration/          # Integration tests with real database
│   └── DatabaseIntegrationTests.cs
└── Performance/          # Performance and load tests
    └── PerformanceTests.cs
```

## Test Framework and Tools

- **xUnit**: Test framework
- **Moq**: Mocking framework for dependencies
- **FluentAssertions**: Fluent assertion library for readable test assertions
- **EF Core InMemory**: In-memory database for unit tests
- **SQLite**: Real database for integration tests
- **Coverlet**: Code coverage tool

## Running Tests

### All Tests
```powershell
cd windows-native/src
dotnet test
```

### By Category

**Unit Tests Only**
```powershell
dotnet test --filter "FullyQualifiedName~UnitTests"
```

**Integration Tests Only**
```powershell
dotnet test --filter "FullyQualifiedName~Integration"
```

**Performance Tests Only**
```powershell
dotnet test --filter "FullyQualifiedName~Performance"
```

### Specific Test Class
```powershell
dotnet test --filter "FullyQualifiedName~QueueServiceTests"
```

### Specific Test Method
```powershell
dotnet test --filter "FullyQualifiedName~QueueServiceTests.AddToQueueAsync_ValidRecording_AddsToQueue"
```

### With Detailed Output
```powershell
dotnet test --logger "console;verbosity=detailed"
```

## Code Coverage

### Generate Coverage Report
```powershell
# Run tests with coverage collection
dotnet test --collect:"XPlat Code Coverage"

# Install report generator (one-time)
dotnet tool install --global dotnet-reportgenerator-globaltool

# Generate HTML report
reportgenerator `
    -reports:**/coverage.cobertura.xml `
    -targetdir:./coverage-report `
    -reporttypes:Html

# Open report
start ./coverage-report/index.html
```

### Coverage Targets
- **Overall**: > 80%
- **Critical Paths**: > 90%
  - Event CRUD operations
  - Audio recording and processing
  - LLM integration and event extraction
  - RAG and similarity search
  - Export/Import operations

## Test Categories Explained

### 1. Unit Tests

Unit tests verify individual components in isolation using mocked dependencies.

**QueueServiceTests** (221 lines, 8 tests)
- ✅ Adding recordings to queue
- ✅ Retrieving queue items
- ✅ Updating queue item status
- ✅ Getting queue count by status
- ✅ Removing queue items
- ✅ Retrying failed items
- ✅ Clearing completed items
- ✅ Event raising on status change

**ExportServiceTests** (254 lines, 8 tests)
- ✅ JSON export with full event data
- ✅ JSON export with date filtering
- ✅ CSV export
- ✅ Markdown export with year organization
- ✅ Full database export (events, eras, tags)
- ✅ Progress reporting
- ✅ Special character escaping in CSV

**ImportServiceTests** (343 lines, 11 tests)
- ✅ Valid JSON import
- ✅ Duplicate detection with skip resolution
- ✅ Overwrite conflict resolution
- ✅ Create duplicate resolution
- ✅ Tag import
- ✅ Era import
- ✅ File validation
- ✅ Invalid file handling
- ✅ Progress reporting

**RagServiceTests** (296 lines, 10 tests)
- ✅ Finding similar events
- ✅ Similarity threshold filtering
- ✅ Tag suggestions based on similar events
- ✅ Pattern detection with date ranges
- ✅ Cross-reference detection
- ✅ Non-existent event handling
- ✅ Embedding similarity calculation

### 2. Integration Tests

Integration tests verify components working together with real database operations.

**DatabaseIntegrationTests** (284 lines, 11 tests)
- ✅ Database connection
- ✅ Full CRUD operations
- ✅ Relationships (events with eras and tags)
- ✅ Pagination (25 events, 3 pages)
- ✅ Date range queries
- ✅ Search functionality
- ✅ Category filtering
- ✅ Transaction rollback on errors
- ✅ Concurrent write operations (10 simultaneous)

### 3. Performance Tests

Performance tests ensure the application meets scalability requirements.

**PerformanceTests** (365 lines, 10 tests)

**Load Tests:**
- ✅ Create 5000 events (< 10 seconds)
- ✅ Query 5000 events (< 2 seconds)
- ✅ Pagination with 5000 events (< 500ms)
- ✅ Date range query on 5000 events (< 1 second)
- ✅ Search on 5000 events (< 1.5 seconds)
- ✅ Category filter on 5000 events (< 1 second)
- ✅ Update 100 events (< 5 seconds)

**Memory Test:**
- ✅ Load 5000 events in memory (< 100 MB with AsNoTracking)

**Concurrency Test:**
- ✅ 20 simultaneous read operations (< 5 seconds)

**Bulk Operations:**
- ✅ Delete 500 events (< 3 seconds)

## Performance Benchmarks

### Target Metrics (5000+ Events)

| Operation | Target | Current |
|-----------|--------|---------|
| Create 5000 events | < 10s | ✅ Pass |
| Query all events | < 2s | ✅ Pass |
| Paginated query (50 items) | < 500ms | ✅ Pass |
| Date range query | < 1s | ✅ Pass |
| Full-text search | < 1.5s | ✅ Pass |
| Category filter | < 1s | ✅ Pass |
| Memory usage | < 100 MB | ✅ Pass |
| Concurrent reads (20) | < 5s | ✅ Pass |

### Database Optimizations
- `AsNoTracking()` on all read-only queries
- Pagination for large datasets
- Indexed columns for common queries
- Connection pooling
- Compiled queries for frequent operations

## Test Data Generators

### Event Generator (Performance Tests)
```csharp
GenerateTestEvents(int count)
```
- Creates realistic test events with varied:
  - Categories (Work, Personal, Education, Health, etc.)
  - Date ranges (5 years)
  - Locations
  - Confidence scores
- Fixed seed (42) for reproducibility

### Embedding Generator (RAG Tests)
```csharp
GenerateMockEmbedding(int seed)
```
- Creates 1536-dimension float arrays
- Seeded for consistent test results
- Simulates OpenAI embedding vectors

## Continuous Integration

### GitHub Actions
Tests run automatically on:
- Every push to main branch
- Every pull request
- Release creation

### Test Results
- Failures block PR merges
- Code coverage reports uploaded to Codecov
- Performance benchmarks tracked over time

## Writing New Tests

### Unit Test Template
```csharp
using FluentAssertions;
using Moq;
using Xunit;

namespace MemoryTimeline.Tests.UnitTests;

public class MyServiceTests : IDisposable
{
    private readonly Mock<IDependency> _dependencyMock;
    private readonly MyService _service;

    public MyServiceTests()
    {
        _dependencyMock = new Mock<IDependency>();
        _service = new MyService(_dependencyMock.Object);
    }

    [Fact]
    public async Task MethodName_Scenario_ExpectedResult()
    {
        // Arrange
        var input = "test";
        _dependencyMock.Setup(d => d.DoSomething(input))
            .ReturnsAsync("result");

        // Act
        var result = await _service.MethodToTest(input);

        // Assert
        result.Should().Be("result");
        _dependencyMock.Verify(d => d.DoSomething(input), Times.Once);
    }

    public void Dispose()
    {
        // Cleanup
    }
}
```

### Integration Test Template
```csharp
public class MyIntegrationTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly string _databasePath;

    public MyIntegrationTests()
    {
        _databasePath = Path.Combine(
            Path.GetTempPath(),
            $"IntegrationTest_{Guid.NewGuid()}.db");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task Integration_Test_Name()
    {
        // Test with real database
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
```

### Performance Test Template
```csharp
public class MyPerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly Stopwatch _stopwatch;

    public MyPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        _stopwatch = new Stopwatch();
    }

    [Fact]
    public async Task PerformanceTest_LargeDataset_CompletesQuickly()
    {
        // Arrange
        var largeDataset = GenerateTestData(10000);

        // Act
        _stopwatch.Start();
        var result = await ProcessLargeDataset(largeDataset);
        _stopwatch.Stop();

        // Assert
        _output.WriteLine($"Processed {largeDataset.Count} items in {_stopwatch.ElapsedMilliseconds}ms");
        _stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000);
    }

    public void Dispose()
    {
        // Cleanup
    }
}
```

## Best Practices

### Test Naming
- Use pattern: `MethodName_Scenario_ExpectedResult`
- Examples:
  - `CreateEvent_ValidData_ReturnsEventWithId`
  - `DeleteEvent_NonExistentId_ThrowsNotFoundException`
  - `SearchEvents_EmptyQuery_ReturnsAllEvents`

### Arrange-Act-Assert Pattern
```csharp
[Fact]
public async Task Example_Test()
{
    // Arrange - Set up test data and mocks
    var input = "test";
    _mock.Setup(m => m.Method(input)).Returns("result");

    // Act - Execute the method under test
    var result = await _service.Method(input);

    // Assert - Verify the results
    result.Should().Be("result");
    _mock.Verify(m => m.Method(input), Times.Once);
}
```

### Fluent Assertions
```csharp
// Prefer FluentAssertions over xUnit assertions
result.Should().NotBeNull();
result.Should().Be(expected);
result.Should().BeEquivalentTo(expected);
collection.Should().HaveCount(5);
collection.Should().Contain(item);
value.Should().BeGreaterThan(10);
exception.Should().BeOfType<ArgumentException>();
```

### Async Tests
```csharp
// Always use async/await for async tests
[Fact]
public async Task AsyncTest()
{
    var result = await _service.MethodAsync();
    result.Should().NotBeNull();
}
```

### Test Data Cleanup
```csharp
public class MyTests : IDisposable
{
    // Use IDisposable for cleanup
    public void Dispose()
    {
        _context?.Dispose();
        DeleteTestFiles();
    }
}
```

## Troubleshooting Tests

### Tests Fail Intermittently
- Check for timing issues in async code
- Use `Task.Delay()` carefully
- Ensure proper disposal of resources
- Check for shared state between tests

### Database Lock Issues
- Ensure each test uses unique database
- Properly dispose DbContext
- Use `Database.EnsureDeleted()` in Dispose

### Performance Tests Failing
- Run on dedicated hardware (not during heavy load)
- Check system resources (CPU, memory, disk)
- Review recent code changes for regressions
- Adjust thresholds if consistently failing

### Coverage Not Generating
```powershell
# Ensure coverlet.collector is installed
dotnet add package coverlet.collector

# Use correct coverage collection
dotnet test --collect:"XPlat Code Coverage"

# Check for .coverage files in TestResults/
dir -Recurse -Filter "*.cobertura.xml"
```

## Test Statistics

Current test suite (as of Phase 7):
- **Total Tests**: ~50+
- **Unit Tests**: ~30
- **Integration Tests**: ~11
- **Performance Tests**: ~10
- **Total LOC**: ~1,500
- **Coverage**: Target > 80%
- **Execution Time**: < 2 minutes for full suite

---

**Last Updated**: 2024-11-21
**Version**: 1.0.0
