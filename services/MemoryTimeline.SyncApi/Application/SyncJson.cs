using System.Text.Json;

namespace MemoryTimeline.SyncApi.Application;

/// <summary>
/// Shared JSON options for manual serialization (change payloads, idempotency
/// replay bodies). camelCase web defaults — identical to the ASP.NET Core
/// response serialization the wire contract relies on.
/// </summary>
public static class SyncJson
{
    /// <summary>camelCase serializer options matching ASP.NET Core defaults.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
