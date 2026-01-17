using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using System.Text.Json;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Service interface for application settings.
/// </summary>
public interface ISettingsService
{
    Task<T?> GetSettingAsync<T>(string key);
    Task<T?> GetSettingAsync<T>(string key, T defaultValue);
    Task SetSettingAsync<T>(string key, T value);
    Task<Dictionary<string, string>> GetAllSettingsAsync();
    Task<bool> SettingExistsAsync(string key);
    Task DeleteSettingAsync(string key);

    // Typed getters for common settings
    Task<string> GetThemeAsync();
    Task SetThemeAsync(string theme);
    Task<string> GetDefaultZoomLevelAsync();
    Task<string> GetLlmProviderAsync();
    Task<string> GetLlmModelAsync();
}

/// <summary>
/// Settings service implementation using database storage.
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly AppDbContext _context;
    private readonly ILogger<SettingsService> _logger;
    private readonly Dictionary<string, string> _cache;
    private bool _cacheInitialized;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public SettingsService(AppDbContext context, ILogger<SettingsService> logger)
    {
        _context = context;
        _logger = logger;
        _cache = new Dictionary<string, string>();
        _cacheInitialized = false;
    }

    /// <summary>
    /// Gets a setting value with optional default.
    /// </summary>
    public async Task<T?> GetSettingAsync<T>(string key)
    {
        return await GetSettingAsync<T>(key, default(T)!);
    }

    /// <summary>
    /// Gets a setting value with a default fallback.
    /// </summary>
    public async Task<T?> GetSettingAsync<T>(string key, T defaultValue)
    {
        try
        {
            await EnsureCacheInitializedAsync();

            if (_cache.TryGetValue(key, out var value))
            {
                return DeserializeValue<T>(value);
            }

            _logger.LogWarning("Setting '{Key}' not found, returning default value", key);
            return defaultValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting setting '{Key}'", key);
            return defaultValue;
        }
    }

    /// <summary>
    /// Sets a setting value.
    /// </summary>
    public async Task SetSettingAsync<T>(string key, T value)
    {
        try
        {
            var serializedValue = SerializeValue(value);

            var setting = await _context.AppSettings.FindAsync(key);

            if (setting != null)
            {
                setting.SettingValue = serializedValue;
                setting.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                setting = new AppSetting
                {
                    SettingKey = key,
                    SettingValue = serializedValue,
                    UpdatedAt = DateTime.UtcNow
                };
                await _context.AppSettings.AddAsync(setting);
            }

            await _context.SaveChangesAsync();

            // Update cache
            await _cacheLock.WaitAsync();
            try
            {
                _cache[key] = serializedValue;
            }
            finally
            {
                _cacheLock.Release();
            }

            _logger.LogInformation("Setting '{Key}' updated successfully", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting '{Key}'", key);
            throw;
        }
    }

    /// <summary>
    /// Gets all settings as a dictionary.
    /// </summary>
    public async Task<Dictionary<string, string>> GetAllSettingsAsync()
    {
        try
        {
            await EnsureCacheInitializedAsync();
            return new Dictionary<string, string>(_cache);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all settings");
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Checks if a setting exists.
    /// </summary>
    public async Task<bool> SettingExistsAsync(string key)
    {
        await EnsureCacheInitializedAsync();
        return _cache.ContainsKey(key);
    }

    /// <summary>
    /// Deletes a setting.
    /// </summary>
    public async Task DeleteSettingAsync(string key)
    {
        try
        {
            var setting = await _context.AppSettings.FindAsync(key);
            if (setting != null)
            {
                _context.AppSettings.Remove(setting);
                await _context.SaveChangesAsync();

                await _cacheLock.WaitAsync();
                try
                {
                    _cache.Remove(key);
                }
                finally
                {
                    _cacheLock.Release();
                }

                _logger.LogInformation("Setting '{Key}' deleted successfully", key);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting setting '{Key}'", key);
            throw;
        }
    }

    // Typed getters for common settings

    public async Task<string> GetThemeAsync()
    {
        return await GetSettingAsync<string>("theme", "dark") ?? "dark";
    }

    public async Task SetThemeAsync(string theme)
    {
        await SetSettingAsync("theme", theme);
    }

    public async Task<string> GetDefaultZoomLevelAsync()
    {
        return await GetSettingAsync<string>("default_zoom_level", "month") ?? "month";
    }

    public async Task<string> GetLlmProviderAsync()
    {
        return await GetSettingAsync<string>("llm_provider", "anthropic") ?? "anthropic";
    }

    public async Task<string> GetLlmModelAsync()
    {
        return await GetSettingAsync<string>("llm_model", "claude-sonnet-4-20250514") ?? "claude-sonnet-4-20250514";
    }

    // Private helper methods

    private async Task EnsureCacheInitializedAsync()
    {
        if (_cacheInitialized)
            return;

        await _cacheLock.WaitAsync();
        try
        {
            if (_cacheInitialized)
                return;

            var settings = await _context.AppSettings.ToListAsync();
            foreach (var setting in settings)
            {
                _cache[setting.SettingKey] = setting.SettingValue;
            }

            _cacheInitialized = true;
            _logger.LogInformation("Settings cache initialized with {Count} settings", _cache.Count);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private string SerializeValue<T>(T value)
    {
        if (value == null)
            return string.Empty;

        if (value is string str)
            return str;

        return JsonSerializer.Serialize(value);
    }

    private T? DeserializeValue<T>(string value)
    {
        if (string.IsNullOrEmpty(value))
            return default;

        if (typeof(T) == typeof(string))
            return (T)(object)value;

        if (typeof(T) == typeof(int) && int.TryParse(value, out var intVal))
            return (T)(object)intVal;

        if (typeof(T) == typeof(double) && double.TryParse(value, out var doubleVal))
            return (T)(object)doubleVal;

        if (typeof(T) == typeof(bool) && bool.TryParse(value, out var boolVal))
            return (T)(object)boolVal;

        try
        {
            return JsonSerializer.Deserialize<T>(value);
        }
        catch
        {
            return default;
        }
    }
}
