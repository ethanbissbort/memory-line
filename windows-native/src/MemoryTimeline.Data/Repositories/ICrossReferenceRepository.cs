using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

public interface ICrossReferenceRepository : IRepository<CrossReference>
{
    Task<IEnumerable<CrossReference>> GetReferencesForEventAsync(string eventId);
    Task<IEnumerable<CrossReference>> GetByRelationshipTypeAsync(RelationshipType relationshipType);
    Task<IEnumerable<CrossReference>> GetHighConfidenceReferencesAsync(double minimumConfidence = 0.7);
    Task<CrossReference?> GetReferenceAsync(string eventId1, string eventId2);
    Task<int> GetReferenceCountForEventAsync(string eventId);
}
