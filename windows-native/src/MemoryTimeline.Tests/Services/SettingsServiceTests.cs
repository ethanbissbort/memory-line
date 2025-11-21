using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.Services;
using MemoryTimeline.Data;
using Moq;
using Xunit;

namespace MemoryTimeline.Tests.Services;

public class SettingsServiceTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly ISettingsService _settingsService;
    private readonly Mock<ILogger<SettingsService>> _loggerMock;

    public SettingsServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
            .Options;

        _context = new AppDbContext(options);
        _loggerMock = new Mock<ILogger<SettingsService>>();
        _settingsService = new SettingsService(_context, _loggerMock.Object);
    }

    [Fact]
    public async Task SetSettingAsync_NewSetting_CreatesSuccessfully()
    {
        // Act
        await _settingsService.SetSettingAsync("test_key", "test_value");

        // Assert
        var result = await _settingsService.GetSettingAsync<string>("test_key");
        result.Should().Be("test_value");
    }

    [Fact]
    public async Task SetSettingAsync_UpdateExistingSetting_UpdatesSuccessfully()
    {
        // Arrange
        await _settingsService.SetSettingAsync("update_key", "original_value");

        // Act
        await _settingsService.SetSettingAsync("update_key", "updated_value");

        // Assert
        var result = await _settingsService.GetSettingAsync<string>("update_key");
        result.Should().Be("updated_value");
    }

    [Fact]
    public async Task GetSettingAsync_NonExistentKey_ReturnsDefaultValue()
    {
        // Act
        var result = await _settingsService.GetSettingAsync("non_existent_key", "default_value");

        // Assert
        result.Should().Be("default_value");
    }

    [Fact]
    public async Task GetSettingAsync_IntValue_ReturnsCorrectType()
    {
        // Arrange
        await _settingsService.SetSettingAsync("int_key", 42);

        // Act
        var result = await _settingsService.GetSettingAsync<int>("int_key");

        // Assert
        result.Should().Be(42);
    }

    [Fact]
    public async Task GetSettingAsync_BoolValue_ReturnsCorrectType()
    {
        // Arrange
        await _settingsService.SetSettingAsync("bool_key", true);

        // Act
        var result = await _settingsService.GetSettingAsync<bool>("bool_key");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task GetAllSettingsAsync_MultipleSettings_ReturnsAllSettings()
    {
        // Arrange
        await _settingsService.SetSettingAsync("key1", "value1");
        await _settingsService.SetSettingAsync("key2", "value2");
        await _settingsService.SetSettingAsync("key3", "value3");

        // Act
        var allSettings = await _settingsService.GetAllSettingsAsync();

        // Assert
        allSettings.Should().HaveCountGreaterOrEqualTo(3);
        allSettings.Should().ContainKey("key1");
        allSettings.Should().ContainKey("key2");
        allSettings.Should().ContainKey("key3");
    }

    [Fact]
    public async Task SettingExistsAsync_ExistingSetting_ReturnsTrue()
    {
        // Arrange
        await _settingsService.SetSettingAsync("exists_key", "some_value");

        // Act
        var exists = await _settingsService.SettingExistsAsync("exists_key");

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task SettingExistsAsync_NonExistentSetting_ReturnsFalse()
    {
        // Act
        var exists = await _settingsService.SettingExistsAsync("non_existent");

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteSettingAsync_ExistingSetting_DeletesSuccessfully()
    {
        // Arrange
        await _settingsService.SetSettingAsync("delete_key", "delete_value");

        // Act
        await _settingsService.DeleteSettingAsync("delete_key");

        // Assert
        var exists = await _settingsService.SettingExistsAsync("delete_key");
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task GetThemeAsync_DefaultTheme_ReturnsLight()
    {
        // Act
        var theme = await _settingsService.GetThemeAsync();

        // Assert
        theme.Should().Be("light");
    }

    [Fact]
    public async Task SetThemeAsync_UpdateTheme_UpdatesSuccessfully()
    {
        // Act
        await _settingsService.SetThemeAsync("dark");

        // Assert
        var theme = await _settingsService.GetThemeAsync();
        theme.Should().Be("dark");
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
