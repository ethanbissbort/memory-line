using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

public class AppSettingRepository : IAppSettingRepository
{
    private readonly AppDbContext _context;

    public AppSettingRepository(AppDbContext context) => _context = context;

    public async Task<AppSetting?> GetByIdAsync(string id) =>
        await _context.AppSettings.FindAsync(id);

    public async Task<IEnumerable<AppSetting>> GetAllAsync() =>
        await _context.AppSettings.OrderBy(s => s.SettingKey).ToListAsync();

    public async Task<AppSetting> AddAsync(AppSetting entity)
    {
        _context.AppSettings.Add(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task UpdateAsync(AppSetting entity)
    {
        _context.AppSettings.Update(entity);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(AppSetting entity)
    {
        _context.AppSettings.Remove(entity);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(string id) =>
        await _context.AppSettings.AnyAsync(s => s.SettingKey == id);

    public async Task<int> CountAsync() =>
        await _context.AppSettings.CountAsync();

    public async Task<IEnumerable<AppSetting>> FindAsync(System.Linq.Expressions.Expression<Func<AppSetting, bool>> predicate) =>
        await _context.AppSettings.Where(predicate).ToListAsync();

    public async Task<AppSetting?> GetByKeyAsync(string key) =>
        await _context.AppSettings.FindAsync(key);

    public async Task<string?> GetValueAsync(string key)
    {
        var setting = await GetByKeyAsync(key);
        return setting?.SettingValue;
    }

    public async Task SetValueAsync(string key, string value)
    {
        var setting = await GetByKeyAsync(key);
        if (setting != null)
        {
            setting.SettingValue = value;
            setting.UpdatedAt = DateTime.UtcNow;
            await UpdateAsync(setting);
        }
        else
        {
            await AddAsync(new AppSetting
            {
                SettingKey = key,
                SettingValue = value,
                UpdatedAt = DateTime.UtcNow
            });
        }
    }

    public async Task<bool> SettingExistsAsync(string key) =>
        await ExistsAsync(key);

    public async Task<IDictionary<string, string>> GetAllSettingsAsync()
    {
        var settings = await GetAllAsync();
        return settings.ToDictionary(s => s.SettingKey, s => s.SettingValue);
    }
}
