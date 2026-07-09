using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

/// <summary>
/// Repository implementation for AppSetting entity.
/// Creates a short-lived <see cref="AppDbContext"/> per operation via
/// <see cref="IDbContextFactory{TContext}"/> so operations are thread-safe
/// and never share change-tracker state.
/// </summary>
public class AppSettingRepository : IAppSettingRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public AppSettingRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<AppSetting?> GetByIdAsync(string id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.AppSettings.FindAsync(id);
    }

    public async Task<IEnumerable<AppSetting>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.AppSettings.AsNoTracking().OrderBy(s => s.SettingKey).ToListAsync();
    }

    public async Task<AppSetting> AddAsync(AppSetting entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.AppSettings.Add(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    public async Task UpdateAsync(AppSetting entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Entity is detached; Update attaches it and marks it Modified.
        context.AppSettings.Update(entity);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(AppSetting entity)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        // Remove attaches the detached entity and marks it Deleted.
        context.AppSettings.Remove(entity);
        await context.SaveChangesAsync();
    }

    public async Task AddRangeAsync(IEnumerable<AppSetting> entities)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.AppSettings.AddRange(entities);
        await context.SaveChangesAsync();
    }

    public async Task DeleteRangeAsync(IEnumerable<AppSetting> entities)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.AppSettings.RemoveRange(entities);
        await context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(System.Linq.Expressions.Expression<Func<AppSetting, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.AppSettings.AnyAsync(predicate);
    }

    public async Task<int> CountAsync(System.Linq.Expressions.Expression<Func<AppSetting, bool>>? predicate = null)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return predicate == null
            ? await context.AppSettings.CountAsync()
            : await context.AppSettings.CountAsync(predicate);
    }

    public async Task<IEnumerable<AppSetting>> FindAsync(System.Linq.Expressions.Expression<Func<AppSetting, bool>> predicate)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.AppSettings.AsNoTracking().Where(predicate).ToListAsync();
    }

    public async Task<AppSetting?> GetByKeyAsync(string key)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.AppSettings.FindAsync(key);
    }

    public async Task<string?> GetValueAsync(string key)
    {
        var setting = await GetByKeyAsync(key);
        return setting?.SettingValue;
    }

    public async Task SetValueAsync(string key, string value)
    {
        // Fetch-then-save happens inside a single context so the read entity
        // is still tracked when SaveChangesAsync runs.
        await using var context = await _contextFactory.CreateDbContextAsync();
        var setting = await context.AppSettings.FindAsync(key);
        if (setting != null)
        {
            setting.SettingValue = value;
            setting.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            context.AppSettings.Add(new AppSetting
            {
                SettingKey = key,
                SettingValue = value,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync();
    }

    public async Task<bool> SettingExistsAsync(string key) =>
        await ExistsAsync(s => s.SettingKey == key);

    public async Task<IDictionary<string, string>> GetAllSettingsAsync()
    {
        var settings = await GetAllAsync();
        return settings.ToDictionary(s => s.SettingKey, s => s.SettingValue);
    }
}
