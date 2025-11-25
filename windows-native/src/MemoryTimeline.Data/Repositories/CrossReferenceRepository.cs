using Microsoft.EntityFrameworkCore;
using MemoryTimeline.Data.Models;

namespace MemoryTimeline.Data.Repositories;

public class CrossReferenceRepository : ICrossReferenceRepository
{
    private readonly AppDbContext _context;

    public CrossReferenceRepository(AppDbContext context) => _context = context;

    public async Task<CrossReference?> GetByIdAsync(string id) =>
        await _context.CrossReferences.Include(cr => cr.Event1).Include(cr => cr.Event2).FirstOrDefaultAsync(cr => cr.ReferenceId == id);

    public async Task<IEnumerable<CrossReference>> GetAllAsync() =>
        await _context.CrossReferences.OrderByDescending(cr => cr.ConfidenceScore).ToListAsync();

    public async Task<CrossReference> AddAsync(CrossReference entity)
    {
        _context.CrossReferences.Add(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task UpdateAsync(CrossReference entity)
    {
        _context.CrossReferences.Update(entity);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(CrossReference entity)
    {
        _context.CrossReferences.Remove(entity);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(string id) =>
        await _context.CrossReferences.AnyAsync(cr => cr.ReferenceId == id);

    public async Task<int> CountAsync() =>
        await _context.CrossReferences.CountAsync();

    public async Task<IEnumerable<CrossReference>> FindAsync(System.Linq.Expressions.Expression<Func<CrossReference, bool>> predicate) =>
        await _context.CrossReferences.Where(predicate).ToListAsync();

    public async Task<IEnumerable<CrossReference>> GetReferencesForEventAsync(string eventId) =>
        await _context.CrossReferences.Where(cr => cr.EventId1 == eventId || cr.EventId2 == eventId)
            .Include(cr => cr.Event1).Include(cr => cr.Event2).OrderByDescending(cr => cr.ConfidenceScore).ToListAsync();

    public async Task<IEnumerable<CrossReference>> GetByRelationshipTypeAsync(RelationshipType relationshipType) =>
        await _context.CrossReferences.Where(cr => cr.RelationshipType == relationshipType)
            .OrderByDescending(cr => cr.ConfidenceScore).ToListAsync();

    public async Task<IEnumerable<CrossReference>> GetHighConfidenceReferencesAsync(double minimumConfidence = 0.7) =>
        await _context.CrossReferences.Where(cr => cr.ConfidenceScore >= minimumConfidence)
            .OrderByDescending(cr => cr.ConfidenceScore).ToListAsync();

    public async Task<CrossReference?> GetReferenceAsync(string eventId1, string eventId2) =>
        await _context.CrossReferences.FirstOrDefaultAsync(cr =>
            (cr.EventId1 == eventId1 && cr.EventId2 == eventId2) || (cr.EventId1 == eventId2 && cr.EventId2 == eventId1));

    public async Task<int> GetReferenceCountForEventAsync(string eventId) =>
        await _context.CrossReferences.CountAsync(cr => cr.EventId1 == eventId || cr.EventId2 == eventId);
}
