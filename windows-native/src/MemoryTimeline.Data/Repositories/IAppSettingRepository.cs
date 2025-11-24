using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

public interface IAppSettingRepository : IRepository<AppSetting>
{
    Task<AppSetting?> GetByKeyAsync(string key);
    Task<string?> GetValueAsync(string key);
    Task SetValueAsync(string key, string value);
    Task<bool> SettingExistsAsync(string key);
    Task<IDictionary<string, string>> GetAllSettingsAsync();
}
