using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Sync;
using Xunit;

namespace MemoryTimeline.Tests.UnitTests;

/// <summary>
/// Tests for <see cref="SettingsSyncCursorStore"/> over the REAL
/// <see cref="SettingsService"/> and the EF InMemory provider (unique database
/// per test instance): default value and long round-trips.
/// </summary>
public class SettingsSyncCursorStoreTests
{
    private readonly SettingsSyncCursorStore _store;

    public SettingsSyncCursorStoreTests()
    {
        var contextFactory = TestDbContextFactory.CreateInMemory();
        var settingsService = new SettingsService(contextFactory, NullLogger<SettingsService>.Instance);
        _store = new SettingsSyncCursorStore(settingsService);
    }

    [Fact]
    public async Task GetCursorAsync_NoStoredValue_ReturnsZero()
    {
        // Assert - a fresh client pulls from the beginning of the change log
        (await _store.GetCursorAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SetCursorAsync_RoundTripsValuesBeyondInt32()
    {
        // Arrange - the change ID is a long; make sure nothing truncates it
        const long cursor = 5_000_000_000L;

        // Act
        await _store.SetCursorAsync(cursor);

        // Assert
        (await _store.GetCursorAsync()).Should().Be(cursor);
    }

    [Fact]
    public async Task SetCursorAsync_Overwrite_LastValueWins()
    {
        // Act
        await _store.SetCursorAsync(7);
        await _store.SetCursorAsync(19);

        // Assert
        (await _store.GetCursorAsync()).Should().Be(19);
    }
}
