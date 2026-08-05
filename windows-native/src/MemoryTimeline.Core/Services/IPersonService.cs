using CommunityToolkit.Mvvm.Messaging;
using MemoryTimeline.Core.DTOs;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Sort options for person lists.
/// </summary>
public enum PersonSortOption
{
    /// <summary>Alphabetically by name.</summary>
    Name,

    /// <summary>Most recently created first.</summary>
    RecentlyAdded,

    /// <summary>Highest linked-event count first.</summary>
    MostEvents,

    /// <summary>Most recently updated first.</summary>
    RecentlyUpdated
}

/// <summary>
/// How a person was matched by <see cref="IPersonService.FindBestMatchAsync"/>.
/// </summary>
public enum PersonMatchKind
{
    /// <summary>Exact case-insensitive name match.</summary>
    Exact,

    /// <summary>Exact case-insensitive nickname match.</summary>
    Nickname,

    /// <summary>Containment or small-edit-distance name match.</summary>
    Fuzzy
}

/// <summary>
/// A matched person together with how the match was made.
/// </summary>
public sealed class PersonMatch
{
    /// <summary>The matched person.</summary>
    public required PersonDto Person { get; init; }

    /// <summary>How the person was matched.</summary>
    public PersonMatchKind Kind { get; init; }
}

/// <summary>
/// A pair of persons that look like duplicates of each other.
/// </summary>
public sealed class PersonDuplicatePair
{
    /// <summary>First person of the pair.</summary>
    public required PersonDto First { get; init; }

    /// <summary>Second person of the pair.</summary>
    public required PersonDto Second { get; init; }

    /// <summary>Human-readable explanation of why the pair is suspicious.</summary>
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Lightweight summary of an event linked to a person.
/// </summary>
public sealed class PersonEventSummary
{
    /// <summary>Event id.</summary>
    public string EventId { get; init; } = string.Empty;

    /// <summary>Event title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Event start date.</summary>
    public DateTime StartDate { get; init; }

    /// <summary>Event end date, if any.</summary>
    public DateTime? EndDate { get; init; }

    /// <summary>Event category.</summary>
    public string? Category { get; init; }

    /// <summary>Start date formatted as "MMM d, yyyy".</summary>
    public string StartDateDisplay => StartDate.ToString("MMM d, yyyy");
}

/// <summary>
/// Service interface for contact-book person business logic.
/// </summary>
public interface IPersonService
{
    /// <summary>Gets all persons with event counts, sorted per the options.</summary>
    Task<List<PersonDto>> GetAllPersonsAsync(PersonSortOption sortBy = PersonSortOption.Name, bool favoritesFirst = true);

    /// <summary>Searches persons by name, nickname, email, company, or relationship.</summary>
    Task<List<PersonDto>> SearchPersonsAsync(string searchTerm, PersonSortOption sortBy = PersonSortOption.Name);

    /// <summary>Gets one person by id (with event count and first/last event dates), or null.</summary>
    Task<PersonDto?> GetPersonAsync(string personId);

    /// <summary>Gets one person by case-insensitive exact name, or null.</summary>
    Task<PersonDto?> GetPersonByNameAsync(string name);

    /// <summary>Gets the persons linked to an event, ordered by name, with event counts.</summary>
    Task<List<PersonDto>> GetPersonsForEventAsync(string eventId);

    /// <summary>Creates a person. Throws <see cref="InvalidOperationException"/> when a person with the same name already exists.</summary>
    Task<PersonDto> CreatePersonAsync(PersonDto person);

    /// <summary>Updates a person's editable fields.</summary>
    Task<PersonDto> UpdatePersonAsync(PersonDto person);

    /// <summary>Deletes a person; event links cascade.</summary>
    Task DeletePersonAsync(string personId);

    /// <summary>Toggles the favorite flag and returns the new state.</summary>
    Task<bool> ToggleFavoriteAsync(string personId);

    /// <summary>Gets summaries of the events linked to a person, newest start date first.</summary>
    Task<List<PersonEventSummary>> GetEventsForPersonAsync(string personId);

    /// <summary>Merges the source person into the target person and deletes the source.</summary>
    Task MergePersonsAsync(string sourcePersonId, string targetPersonId);

    /// <summary>Finds pairs of persons that look like duplicates.</summary>
    Task<List<PersonDuplicatePair>> FindPotentialDuplicatesAsync();

    /// <summary>Finds the best existing person for a free-text name, or null when nothing plausible matches.</summary>
    Task<PersonMatch?> FindBestMatchAsync(string name);
}

/// <summary>
/// Person service implementation. Uses short-lived contexts from the factory
/// per operation; event counts and date ranges come from a single grouped
/// query over the event_people junction (no N+1).
/// </summary>
public class PersonService : IPersonService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<PersonService> _logger;

    public PersonService(IDbContextFactory<AppDbContext> contextFactory, ILogger<PersonService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<PersonDto>> GetAllPersonsAsync(
        PersonSortOption sortBy = PersonSortOption.Name,
        bool favoritesFirst = true)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var people = await context.People.AsNoTracking().ToListAsync();
            var stats = await LoadEventStatsAsync(context);

            var dtos = people.Select(p => ToDto(p, stats));
            return SortPersons(dtos, sortBy, favoritesFirst);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all persons");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<List<PersonDto>> SearchPersonsAsync(
        string searchTerm,
        PersonSortOption sortBy = PersonSortOption.Name)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return await GetAllPersonsAsync(sortBy);
            }

            await using var context = await _contextFactory.CreateDbContextAsync();

            var people = await context.People.AsNoTracking().ToListAsync();
            var stats = await LoadEventStatsAsync(context);

            var term = searchTerm.Trim();
            var matches = people
                .Where(p =>
                    ContainsIgnoreCase(p.Name, term) ||
                    ContainsIgnoreCase(p.Nickname, term) ||
                    ContainsIgnoreCase(p.Email, term) ||
                    ContainsIgnoreCase(p.Company, term) ||
                    ContainsIgnoreCase(p.Relationship, term))
                .Select(p => ToDto(p, stats));

            return SortPersons(matches, sortBy, favoritesFirst: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching persons: {SearchTerm}", searchTerm);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<PersonDto?> GetPersonAsync(string personId)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var person = await context.People.AsNoTracking()
                .FirstOrDefaultAsync(p => p.PersonId == personId);
            if (person == null)
            {
                return null;
            }

            var stats = await LoadEventStatsAsync(context, personId);
            return ToDto(person, stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting person: {PersonId}", personId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<PersonDto?> GetPersonByNameAsync(string name)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            await using var context = await _contextFactory.CreateDbContextAsync();

            var lowered = name.Trim().ToLowerInvariant();
            var person = await context.People.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Name.ToLower() == lowered);
            if (person == null)
            {
                return null;
            }

            var stats = await LoadEventStatsAsync(context, person.PersonId);
            return ToDto(person, stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting person by name: {Name}", name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<List<PersonDto>> GetPersonsForEventAsync(string eventId)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var people = await context.EventPeople.AsNoTracking()
                .Where(ep => ep.EventId == eventId)
                .Select(ep => ep.Person)
                .OrderBy(p => p.Name)
                .ToListAsync();
            if (people.Count == 0)
            {
                return new List<PersonDto>();
            }

            var stats = await LoadEventStatsAsync(context);
            return people.Select(p => ToDto(p, stats)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting persons for event: {EventId}", eventId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<PersonDto> CreatePersonAsync(PersonDto person)
    {
        try
        {
            var trimmedName = person.Name.Trim();
            if (trimmedName.Length == 0)
            {
                throw new ArgumentException("Person name is required", nameof(person));
            }

            await using var context = await _contextFactory.CreateDbContextAsync();

            var lowered = trimmedName.ToLowerInvariant();
            if (await context.People.AsNoTracking().AnyAsync(p => p.Name.ToLower() == lowered))
            {
                throw new InvalidOperationException($"A person named '{trimmedName}' already exists.");
            }

            var entity = person.ToPerson();
            entity.Name = trimmedName;
            if (string.IsNullOrWhiteSpace(entity.PersonId))
            {
                entity.PersonId = Guid.NewGuid().ToString();
            }
            entity.CreatedAt = DateTime.UtcNow;
            entity.UpdatedAt = DateTime.UtcNow;

            context.People.Add(entity);
            await context.SaveChangesAsync();

            _logger.LogInformation("Person created: {PersonId} - {Name}", entity.PersonId, entity.Name);
            SendMessage(new PersonCreatedMessage(entity.PersonId));

            return PersonDto.FromPerson(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating person: {Name}", person.Name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<PersonDto> UpdatePersonAsync(PersonDto person)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var entity = await context.People
                .FirstOrDefaultAsync(p => p.PersonId == person.PersonId);
            if (entity == null)
            {
                throw new InvalidOperationException($"Person not found: {person.PersonId}");
            }

            person.CopyTo(entity);
            entity.Name = entity.Name.Trim();
            entity.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();

            _logger.LogInformation("Person updated: {PersonId} - {Name}", entity.PersonId, entity.Name);
            SendMessage(new PersonUpdatedMessage(entity.PersonId));

            var stats = await LoadEventStatsAsync(context, entity.PersonId);
            return ToDto(entity, stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating person: {PersonId}", person.PersonId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeletePersonAsync(string personId)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var entity = await context.People.FirstOrDefaultAsync(p => p.PersonId == personId);
            if (entity == null)
            {
                throw new InvalidOperationException($"Person not found: {personId}");
            }

            context.People.Remove(entity);
            await context.SaveChangesAsync();

            _logger.LogInformation("Person deleted: {PersonId}", personId);
            SendMessage(new PersonDeletedMessage(personId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting person: {PersonId}", personId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ToggleFavoriteAsync(string personId)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var entity = await context.People.FirstOrDefaultAsync(p => p.PersonId == personId);
            if (entity == null)
            {
                throw new InvalidOperationException($"Person not found: {personId}");
            }

            entity.IsFavorite = !entity.IsFavorite;
            entity.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();

            _logger.LogInformation(
                "Person favorite toggled: {PersonId} -> {IsFavorite}", personId, entity.IsFavorite);
            SendMessage(new PersonUpdatedMessage(personId));

            return entity.IsFavorite;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling favorite for person: {PersonId}", personId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<List<PersonEventSummary>> GetEventsForPersonAsync(string personId)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var events = await context.EventPeople.AsNoTracking()
                .Where(ep => ep.PersonId == personId)
                .Select(ep => new
                {
                    ep.Event.EventId,
                    ep.Event.Title,
                    ep.Event.StartDate,
                    ep.Event.EndDate,
                    ep.Event.Category
                })
                .OrderByDescending(e => e.StartDate)
                .ToListAsync();

            return events
                .Select(e => new PersonEventSummary
                {
                    EventId = e.EventId,
                    Title = e.Title,
                    StartDate = e.StartDate,
                    EndDate = e.EndDate,
                    Category = e.Category
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting events for person: {PersonId}", personId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task MergePersonsAsync(string sourcePersonId, string targetPersonId)
    {
        try
        {
            if (string.Equals(sourcePersonId, targetPersonId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Cannot merge a person into itself.");
            }

            await using var context = await _contextFactory.CreateDbContextAsync();
            await using var transaction = await context.Database.BeginTransactionAsync();

            var source = await context.People.FirstOrDefaultAsync(p => p.PersonId == sourcePersonId);
            if (source == null)
            {
                throw new InvalidOperationException($"Person not found: {sourcePersonId}");
            }

            var target = await context.People.FirstOrDefaultAsync(p => p.PersonId == targetPersonId);
            if (target == null)
            {
                throw new InvalidOperationException($"Person not found: {targetPersonId}");
            }

            // Repoint junction rows from source to target. The composite PK
            // (EventId, PersonId) cannot be updated in place, so rows are
            // removed and re-added; rows that would duplicate an existing
            // target link are simply dropped.
            var sourceLinks = await context.EventPeople
                .Where(ep => ep.PersonId == sourcePersonId)
                .ToListAsync();
            var targetEventIds = (await context.EventPeople.AsNoTracking()
                    .Where(ep => ep.PersonId == targetPersonId)
                    .Select(ep => ep.EventId)
                    .ToListAsync())
                .ToHashSet(StringComparer.Ordinal);

            foreach (var link in sourceLinks)
            {
                context.EventPeople.Remove(link);
                if (!targetEventIds.Contains(link.EventId))
                {
                    context.EventPeople.Add(new EventPerson
                    {
                        EventId = link.EventId,
                        PersonId = targetPersonId,
                        CreatedAt = link.CreatedAt
                    });
                    targetEventIds.Add(link.EventId);
                }
            }

            // Backfill contact fields the target lacks with the source's values.
            target.Nickname = Backfill(target.Nickname, source.Nickname);
            target.Relationship = Backfill(target.Relationship, source.Relationship);
            target.Email = Backfill(target.Email, source.Email);
            target.Phone = Backfill(target.Phone, source.Phone);
            target.Birthday ??= source.Birthday;
            target.Company = Backfill(target.Company, source.Company);
            target.Notes = Backfill(target.Notes, source.Notes);
            target.PhotoPath = Backfill(target.PhotoPath, source.PhotoPath);
            target.AvatarColor = Backfill(target.AvatarColor, source.AvatarColor);
            target.FirstMetDate ??= source.FirstMetDate;
            if (source.IsFavorite)
            {
                target.IsFavorite = true;
            }
            target.UpdatedAt = DateTime.UtcNow;

            context.People.Remove(source);

            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation(
                "Persons merged: {SourcePersonId} -> {TargetPersonId}", sourcePersonId, targetPersonId);
            SendMessage(new PersonsMergedMessage(sourcePersonId, targetPersonId));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "Error merging person {SourcePersonId} into {TargetPersonId}",
                sourcePersonId, targetPersonId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<List<PersonDuplicatePair>> FindPotentialDuplicatesAsync()
    {
        try
        {
            var people = await GetAllPersonsAsync(PersonSortOption.Name, favoritesFirst: false);
            var pairs = new List<PersonDuplicatePair>();

            for (var i = 0; i < people.Count; i++)
            {
                for (var j = i + 1; j < people.Count; j++)
                {
                    var a = people[i];
                    var b = people[j];
                    var reason = GetDuplicateReason(a, b);
                    if (reason != null)
                    {
                        pairs.Add(new PersonDuplicatePair { First = a, Second = b, Reason = reason });
                    }
                }
            }

            return pairs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding potential duplicate persons");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<PersonMatch?> FindBestMatchAsync(string name)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var people = await GetAllPersonsAsync(PersonSortOption.Name, favoritesFirst: false);
            var trimmed = name.Trim();
            var normalized = Normalize(trimmed);

            // 1. Exact name match (case-insensitive).
            var exact = people.FirstOrDefault(p =>
                string.Equals(p.Name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                return new PersonMatch { Person = exact, Kind = PersonMatchKind.Exact };
            }

            // 2. Exact nickname match (case-insensitive).
            var byNickname = people.FirstOrDefault(p =>
                !string.IsNullOrWhiteSpace(p.Nickname) &&
                string.Equals(p.Nickname.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
            if (byNickname != null)
            {
                return new PersonMatch { Person = byNickname, Kind = PersonMatchKind.Nickname };
            }

            // 3. Fuzzy: normalized containment or Levenshtein distance <= 2.
            PersonDto? best = null;
            var bestScore = int.MaxValue;
            foreach (var candidate in people)
            {
                var candidateNorm = Normalize(candidate.Name);
                if (candidateNorm.Length == 0)
                {
                    continue;
                }

                int score;
                if (normalized.Length >= 2 &&
                    (candidateNorm.Contains(normalized, StringComparison.Ordinal) ||
                     normalized.Contains(candidateNorm, StringComparison.Ordinal)))
                {
                    // Containment beats any edit-distance match.
                    score = 0;
                }
                else
                {
                    var distance = LevenshteinDistance(normalized, candidateNorm);
                    if (distance > 2)
                    {
                        continue;
                    }
                    score = distance;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best == null ? null : new PersonMatch { Person = best, Kind = PersonMatchKind.Fuzzy };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding best person match for: {Name}", name);
            throw;
        }
    }

    // Private helpers

    /// <summary>
    /// Loads per-person event statistics (count plus first/last event start
    /// date) in a single grouped join query over event_people and events.
    /// </summary>
    private static async Task<Dictionary<string, (int Count, DateTime First, DateTime Last)>> LoadEventStatsAsync(
        AppDbContext context,
        string? personId = null)
    {
        var links = context.EventPeople.AsNoTracking();
        if (personId != null)
        {
            links = links.Where(ep => ep.PersonId == personId);
        }

        var stats = await links
            .Join(
                context.Events.AsNoTracking(),
                ep => ep.EventId,
                e => e.EventId,
                (ep, e) => new { ep.PersonId, e.StartDate })
            .GroupBy(x => x.PersonId)
            .Select(g => new
            {
                PersonId = g.Key,
                Count = g.Count(),
                First = g.Min(x => x.StartDate),
                Last = g.Max(x => x.StartDate)
            })
            .ToListAsync();

        return stats.ToDictionary(
            s => s.PersonId,
            s => (s.Count, s.First, s.Last),
            StringComparer.Ordinal);
    }

    /// <summary>Maps an entity to a DTO, filling event statistics from the lookup.</summary>
    private static PersonDto ToDto(
        Person person,
        Dictionary<string, (int Count, DateTime First, DateTime Last)> stats)
    {
        return stats.TryGetValue(person.PersonId, out var s)
            ? PersonDto.FromPerson(person, s.Count, s.First, s.Last)
            : PersonDto.FromPerson(person);
    }

    /// <summary>Sorts persons by the requested option, favorites first when asked.</summary>
    private static List<PersonDto> SortPersons(
        IEnumerable<PersonDto> people,
        PersonSortOption sortBy,
        bool favoritesFirst)
    {
        var ordered = favoritesFirst
            ? people.OrderByDescending(p => p.IsFavorite)
            : people.OrderBy(_ => 0);

        return sortBy switch
        {
            PersonSortOption.RecentlyAdded => ordered
                .ThenByDescending(p => p.CreatedAt)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            PersonSortOption.MostEvents => ordered
                .ThenByDescending(p => p.EventCount)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            PersonSortOption.RecentlyUpdated => ordered
                .ThenByDescending(p => p.UpdatedAt)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            _ => ordered
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    /// <summary>
    /// Returns a human-readable reason when the two persons look like
    /// duplicates, or null when they do not.
    /// </summary>
    private static string? GetDuplicateReason(PersonDto a, PersonDto b)
    {
        var nameA = Normalize(a.Name);
        var nameB = Normalize(b.Name);
        if (nameA.Length == 0 || nameB.Length == 0)
        {
            return null;
        }

        if (nameA == nameB)
        {
            return $"Names match: '{a.Name}' and '{b.Name}'";
        }

        var nicknameA = Normalize(a.Nickname ?? string.Empty);
        var nicknameB = Normalize(b.Nickname ?? string.Empty);
        if (nicknameA.Length > 0 && nicknameA == nameB)
        {
            return $"Nickname '{a.Nickname}' of '{a.Name}' matches name '{b.Name}'";
        }
        if (nicknameB.Length > 0 && nicknameB == nameA)
        {
            return $"Nickname '{b.Nickname}' of '{b.Name}' matches name '{a.Name}'";
        }

        if (LevenshteinDistance(nameA, nameB) <= 1)
        {
            return $"Names are very similar: '{a.Name}' and '{b.Name}'";
        }

        return null;
    }

    /// <summary>Trims, lowercases, and collapses inner whitespace for comparisons.</summary>
    private static string Normalize(string value)
    {
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(' ', words).ToLowerInvariant();
    }

    /// <summary>Case-insensitive substring check that tolerates null haystacks.</summary>
    private static bool ContainsIgnoreCase(string? haystack, string needle)
    {
        return !string.IsNullOrEmpty(haystack) &&
               haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns the source value when the target value is null or whitespace.</summary>
    private static string? Backfill(string? targetValue, string? sourceValue)
    {
        return string.IsNullOrWhiteSpace(targetValue) && !string.IsNullOrWhiteSpace(sourceValue)
            ? sourceValue
            : targetValue;
    }

    /// <summary>
    /// Standard two-row Levenshtein edit distance between two strings.
    /// </summary>
    private static int LevenshteinDistance(string a, string b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }
        if (b.Length == 0)
        {
            return a.Length;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var substitutionCost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    /// <summary>
    /// Publishes a messenger notification; a subscriber failure must not turn
    /// a successful write into an error.
    /// </summary>
    private void SendMessage<TMessage>(TMessage message) where TMessage : class
    {
        try
        {
            WeakReferenceMessenger.Default.Send(message);
        }
        catch (Exception messengerEx)
        {
            _logger.LogWarning(
                messengerEx, "Error publishing {MessageType}", typeof(TMessage).Name);
        }
    }
}
