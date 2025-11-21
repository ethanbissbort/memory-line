namespace MemoryTimeline.Core.Services;

/// <summary>
/// Service interface for application settings.
/// </summary>
public interface ISettingsService
{
    Task<T?> GetSettingAsync<T>(string key);
    Task SetSettingAsync<T>(string key, T value);
}

/// <summary>
/// Settings service implementation using Windows.Storage and database.
/// </summary>
public class SettingsService : ISettingsService
{
    public Task<T?> GetSettingAsync<T>(string key)
    {
        // TODO: Implement
        throw new NotImplementedException();
    }

    public Task SetSettingAsync<T>(string key, T value)
    {
        // TODO: Implement
        throw new NotImplementedException();
    }
}
