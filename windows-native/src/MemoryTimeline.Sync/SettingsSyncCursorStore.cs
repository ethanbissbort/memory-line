using MemoryTimeline.Core.Services;

namespace MemoryTimeline.Sync;

/// <summary>
/// <see cref="ISyncCursorStore"/> persisting the last durably-applied server
/// change ID in the app settings table under <see cref="SettingKeys.SyncCursor"/>
/// (design §12.1: the local cursor is authoritative). Defaults to 0 — pull
/// everything — when no cursor has been stored yet.
/// </summary>
public class SettingsSyncCursorStore : ISyncCursorStore
{
    private readonly ISettingsService _settingsService;

    public SettingsSyncCursorStore(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <inheritdoc />
    public async Task<long> GetCursorAsync() =>
        await _settingsService.GetSettingAsync(SettingKeys.SyncCursor, 0L);

    /// <inheritdoc />
    public Task SetCursorAsync(long cursor) =>
        _settingsService.SetSettingAsync(SettingKeys.SyncCursor, cursor);
}
