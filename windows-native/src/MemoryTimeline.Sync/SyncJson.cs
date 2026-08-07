using System.Text.Json;

namespace MemoryTimeline.Sync;

/// <summary>
/// Shared wire-JSON options for the sync clients: camelCase web defaults,
/// matching the ASP.NET Core serializer configuration on the service side
/// (see shared-contracts/dotnet/MemoryTimeline.SyncContracts).
/// </summary>
internal static class SyncJson
{
    /// <summary>Serializer options used for every sync API request/response/payload body.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
