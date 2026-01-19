using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MemoryTimeline.Core.Models;
using MemoryTimeline.Data;
using MemoryTimeline.Data.Models;
using System.Diagnostics;
using System.Text.Json;

namespace MemoryTimeline.Core.Services;

/// <summary>
/// Service interface for advanced search operations.
/// </summary>
public interface IAdvancedSearchService
{
    // Faceted search
    Task<SearchResults> SearchAsync(SearchFilter filter);
    Task<SearchFacets> GetFacetsAsync(SearchFilter? baseFilter = null);

    // Autocomplete
    Task<List<AutocompleteSuggestion>> GetAutocompleteSuggestionsAsync(string query, int maxResults = 10);

    // Saved searches
    Task<SavedSearch> SaveSearchAsync(string name, SearchFilter filter, bool isFavorite = false);
    Task<SavedSearch?> GetSavedSearchAsync(string savedSearchId);
    Task<List<SavedSearch>> GetAllSavedSearchesAsync();
    Task<List<SavedSearch>> GetFavoriteSavedSearchesAsync();
    Task<List<SavedSearch>> GetRecentSavedSearchesAsync(int count = 10);
    Task UpdateSavedSearchAsync(SavedSearch savedSearch);
    Task DeleteSavedSearchAsync(string savedSearchId);
    Task MarkSearchAsUsedAsync(string savedSearchId);

    // Convert between filter and saved search
    SearchFilter ConvertToFilter(SavedSearch savedSearch);
}

/// <summary>
/// Advanced search service implementation with faceted search and autocomplete.
/// </summary>
public class AdvancedSearchService : IAdvancedSearchService
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<AdvancedSearchService> _logger;

    public AdvancedSearchService(AppDbContext dbContext, ILogger<AdvancedSearchService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    #region Faceted Search

    public async Task<SearchResults> SearchAsync(SearchFilter filter)
    {
        var stopwatch = Stopwatch.StartNew();
        var results = new SearchResults
        {
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize
        };

        try
        {
            var query = _dbContext.Events
                .AsNoTracking() // Ensure fresh data from database, avoid caching issues
                .Include(e => e.EventTags).ThenInclude(et => et.Tag)
                .Include(e => e.EventPeople).ThenInclude(ep => ep.Person)
                .Include(e => e.EventLocations).ThenInclude(el => el.Location)
                .Include(e => e.Era)
                .AsQueryable();

            // Apply filters
            query = ApplyFilters(query, filter);

            // Get total count before pagination
            results.TotalCount = await query.CountAsync();

            // Apply sorting
            query = ApplySorting(query, filter.SortBy);

            // Apply pagination
            var skip = (filter.PageNumber - 1) * filter.PageSize;
            query = query.Skip(skip).Take(filter.PageSize);

            results.Events = await query.ToListAsync();

            // Get facets for current filter context (without current selections)
            results.Facets = await GetFacetsAsync(filter);

            stopwatch.Stop();
            results.SearchDuration = stopwatch.Elapsed;

            _logger.LogInformation(
                "Search completed: {Count} results in {Duration}ms",
                results.TotalCount, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing search");
            throw;
        }

        return results;
    }

    private IQueryable<Event> ApplyFilters(IQueryable<Event> query, SearchFilter filter)
    {
        // Text search
        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.ToLower();
            query = query.Where(e =>
                e.Title.ToLower().Contains(term) ||
                (e.Description != null && e.Description.ToLower().Contains(term)) ||
                (e.RawTranscript != null && e.RawTranscript.ToLower().Contains(term)) ||
                (e.Location != null && e.Location.ToLower().Contains(term)));
        }

        // Category filter
        if (filter.Categories.Any())
        {
            query = query.Where(e => e.Category != null && filter.Categories.Contains(e.Category));
        }

        // Tag filter
        if (filter.TagIds.Any())
        {
            query = query.Where(e => e.EventTags.Any(et => filter.TagIds.Contains(et.TagId)));
        }

        // Person filter
        if (filter.PersonIds.Any())
        {
            query = query.Where(e => e.EventPeople.Any(ep => filter.PersonIds.Contains(ep.PersonId)));
        }

        // Location filter
        if (filter.LocationIds.Any())
        {
            query = query.Where(e => e.EventLocations.Any(el => filter.LocationIds.Contains(el.LocationId)));
        }

        // Era filter
        if (filter.EraIds.Any())
        {
            query = query.Where(e => e.EraId != null && filter.EraIds.Contains(e.EraId));
        }

        // Date range filter
        if (filter.StartDate.HasValue)
        {
            query = query.Where(e => e.StartDate >= filter.StartDate.Value);
        }
        if (filter.EndDate.HasValue)
        {
            query = query.Where(e => e.StartDate <= filter.EndDate.Value);
        }

        // Has audio filter
        if (filter.HasAudio.HasValue)
        {
            if (filter.HasAudio.Value)
                query = query.Where(e => e.AudioFilePath != null && e.AudioFilePath != "");
            else
                query = query.Where(e => e.AudioFilePath == null || e.AudioFilePath == "");
        }

        // Has transcript filter
        if (filter.HasTranscript.HasValue)
        {
            if (filter.HasTranscript.Value)
                query = query.Where(e => e.RawTranscript != null && e.RawTranscript != "");
            else
                query = query.Where(e => e.RawTranscript == null || e.RawTranscript == "");
        }

        // Min confidence filter
        if (filter.MinConfidence.HasValue)
        {
            query = query.Where(e => e.Confidence == null || e.Confidence >= filter.MinConfidence.Value);
        }

        return query;
    }

    private IQueryable<Event> ApplySorting(IQueryable<Event> query, string sortBy)
    {
        return sortBy switch
        {
            SearchSortOptions.DateDesc => query.OrderByDescending(e => e.StartDate),
            SearchSortOptions.DateAsc => query.OrderBy(e => e.StartDate),
            SearchSortOptions.TitleAsc => query.OrderBy(e => e.Title),
            SearchSortOptions.TitleDesc => query.OrderByDescending(e => e.Title),
            SearchSortOptions.CreatedDesc => query.OrderByDescending(e => e.CreatedAt),
            SearchSortOptions.CreatedAsc => query.OrderBy(e => e.CreatedAt),
            SearchSortOptions.Relevance => query.OrderByDescending(e => e.StartDate), // Default for now
            _ => query.OrderByDescending(e => e.StartDate)
        };
    }

    public async Task<SearchFacets> GetFacetsAsync(SearchFilter? baseFilter = null)
    {
        var facets = new SearchFacets();

        try
        {
            var baseQuery = _dbContext.Events.AsQueryable();

            // Apply base filter if provided (excluding the facet being calculated)
            if (baseFilter != null)
            {
                if (!string.IsNullOrWhiteSpace(baseFilter.SearchTerm))
                {
                    var term = baseFilter.SearchTerm.ToLower();
                    baseQuery = baseQuery.Where(e =>
                        e.Title.ToLower().Contains(term) ||
                        (e.Description != null && e.Description.ToLower().Contains(term)));
                }
                if (baseFilter.StartDate.HasValue)
                    baseQuery = baseQuery.Where(e => e.StartDate >= baseFilter.StartDate.Value);
                if (baseFilter.EndDate.HasValue)
                    baseQuery = baseQuery.Where(e => e.StartDate <= baseFilter.EndDate.Value);
            }

            // Category facets
            facets.Categories = await baseQuery
                .Where(e => e.Category != null)
                .GroupBy(e => e.Category!)
                .Select(g => new { Category = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Category, x => x.Count);

            // Tag facets
            var tagCounts = await _dbContext.EventTags
                .Where(et => baseQuery.Any(e => e.EventId == et.EventId))
                .Include(et => et.Tag)
                .GroupBy(et => new { et.TagId, et.Tag.Name, et.Tag.Color })
                .Select(g => new { g.Key.TagId, g.Key.Name, g.Key.Color, Count = g.Count() })
                .ToListAsync();

            facets.Tags = tagCounts.ToDictionary(
                x => x.TagId,
                x => new FacetItem { Id = x.TagId, Name = x.Name, Count = x.Count, Color = x.Color });

            // People facets
            var personCounts = await _dbContext.EventPeople
                .Where(ep => baseQuery.Any(e => e.EventId == ep.EventId))
                .Include(ep => ep.Person)
                .GroupBy(ep => new { ep.PersonId, ep.Person.Name })
                .Select(g => new { g.Key.PersonId, g.Key.Name, Count = g.Count() })
                .ToListAsync();

            facets.People = personCounts.ToDictionary(
                x => x.PersonId,
                x => new FacetItem { Id = x.PersonId, Name = x.Name, Count = x.Count });

            // Location facets
            var locationCounts = await _dbContext.EventLocations
                .Where(el => baseQuery.Any(e => e.EventId == el.EventId))
                .Include(el => el.Location)
                .GroupBy(el => new { el.LocationId, el.Location.Name })
                .Select(g => new { g.Key.LocationId, g.Key.Name, Count = g.Count() })
                .ToListAsync();

            facets.Locations = locationCounts.ToDictionary(
                x => x.LocationId,
                x => new FacetItem { Id = x.LocationId, Name = x.Name, Count = x.Count });

            // Era facets
            var eraCounts = await baseQuery
                .Where(e => e.EraId != null && e.Era != null)
                .GroupBy(e => new { e.EraId, e.Era!.Name, e.Era.Color })
                .Select(g => new { EraId = g.Key.EraId!, g.Key.Name, g.Key.Color, Count = g.Count() })
                .ToListAsync();

            facets.Eras = eraCounts.ToDictionary(
                x => x.EraId,
                x => new FacetItem { Id = x.EraId, Name = x.Name, Count = x.Count, Color = x.Color });

            // Audio/transcript counts
            facets.WithAudioCount = await baseQuery.CountAsync(e => e.AudioFilePath != null && e.AudioFilePath != "");
            facets.WithTranscriptCount = await baseQuery.CountAsync(e => e.RawTranscript != null && e.RawTranscript != "");

            // Date range
            facets.EarliestDate = await baseQuery.MinAsync(e => (DateTime?)e.StartDate);
            facets.LatestDate = await baseQuery.MaxAsync(e => (DateTime?)e.StartDate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating facets");
        }

        return facets;
    }

    #endregion

    #region Autocomplete

    public async Task<List<AutocompleteSuggestion>> GetAutocompleteSuggestionsAsync(string query, int maxResults = 10)
    {
        var suggestions = new List<AutocompleteSuggestion>();

        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return suggestions;

        try
        {
            var lowerQuery = query.ToLower();
            var halfResults = maxResults / 2;

            // Event title suggestions
            var eventSuggestions = await _dbContext.Events
                .Where(e => e.Title.ToLower().Contains(lowerQuery))
                .OrderByDescending(e => e.StartDate)
                .Take(halfResults)
                .Select(e => new AutocompleteSuggestion
                {
                    Text = e.Title,
                    Type = "event",
                    Id = e.EventId,
                    Context = e.StartDate.ToString("MMM yyyy")
                })
                .ToListAsync();
            suggestions.AddRange(eventSuggestions);

            // Tag suggestions
            var tagSuggestions = await _dbContext.Tags
                .Where(t => t.Name.ToLower().Contains(lowerQuery))
                .Take(halfResults / 2)
                .Select(t => new AutocompleteSuggestion
                {
                    Text = t.Name,
                    Type = "tag",
                    Id = t.TagId
                })
                .ToListAsync();
            suggestions.AddRange(tagSuggestions);

            // Person suggestions
            var personSuggestions = await _dbContext.People
                .Where(p => p.Name.ToLower().Contains(lowerQuery))
                .Take(halfResults / 2)
                .Select(p => new AutocompleteSuggestion
                {
                    Text = p.Name,
                    Type = "person",
                    Id = p.PersonId
                })
                .ToListAsync();
            suggestions.AddRange(personSuggestions);

            // Location suggestions
            var locationSuggestions = await _dbContext.Locations
                .Where(l => l.Name.ToLower().Contains(lowerQuery))
                .Take(halfResults / 2)
                .Select(l => new AutocompleteSuggestion
                {
                    Text = l.Name,
                    Type = "location",
                    Id = l.LocationId
                })
                .ToListAsync();
            suggestions.AddRange(locationSuggestions);

            // Era suggestions
            var eraSuggestions = await _dbContext.Eras
                .Where(e => e.Name.ToLower().Contains(lowerQuery))
                .Take(halfResults / 2)
                .Select(e => new AutocompleteSuggestion
                {
                    Text = e.Name,
                    Type = "era",
                    Id = e.EraId
                })
                .ToListAsync();
            suggestions.AddRange(eraSuggestions);

            // Score and sort by relevance (prefix match scores higher)
            foreach (var suggestion in suggestions)
            {
                suggestion.Score = suggestion.Text.ToLower().StartsWith(lowerQuery) ? 2.0 : 1.0;
            }

            return suggestions
                .OrderByDescending(s => s.Score)
                .ThenBy(s => s.Text)
                .Take(maxResults)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting autocomplete suggestions for: {Query}", query);
            return suggestions;
        }
    }

    #endregion

    #region Saved Searches

    public async Task<SavedSearch> SaveSearchAsync(string name, SearchFilter filter, bool isFavorite = false)
    {
        try
        {
            var savedSearch = new SavedSearch
            {
                SavedSearchId = Guid.NewGuid().ToString(),
                Name = name,
                SearchTerm = filter.SearchTerm,
                Categories = filter.Categories.Any() ? JsonSerializer.Serialize(filter.Categories) : null,
                TagIds = filter.TagIds.Any() ? JsonSerializer.Serialize(filter.TagIds) : null,
                PersonIds = filter.PersonIds.Any() ? JsonSerializer.Serialize(filter.PersonIds) : null,
                LocationIds = filter.LocationIds.Any() ? JsonSerializer.Serialize(filter.LocationIds) : null,
                EraIds = filter.EraIds.Any() ? JsonSerializer.Serialize(filter.EraIds) : null,
                StartDate = filter.StartDate,
                EndDate = filter.EndDate,
                HasAudio = filter.HasAudio,
                HasTranscript = filter.HasTranscript,
                MinConfidence = filter.MinConfidence,
                SortBy = filter.SortBy,
                PageSize = filter.PageSize,
                IsFavorite = isFavorite,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.SavedSearches.Add(savedSearch);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Saved search created: {Name}", name);
            return savedSearch;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving search: {Name}", name);
            throw;
        }
    }

    public async Task<SavedSearch?> GetSavedSearchAsync(string savedSearchId)
    {
        return await _dbContext.SavedSearches.FindAsync(savedSearchId);
    }

    public async Task<List<SavedSearch>> GetAllSavedSearchesAsync()
    {
        return await _dbContext.SavedSearches
            .OrderByDescending(s => s.IsFavorite)
            .ThenByDescending(s => s.LastUsedAt ?? s.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<SavedSearch>> GetFavoriteSavedSearchesAsync()
    {
        return await _dbContext.SavedSearches
            .Where(s => s.IsFavorite)
            .OrderByDescending(s => s.UseCount)
            .ToListAsync();
    }

    public async Task<List<SavedSearch>> GetRecentSavedSearchesAsync(int count = 10)
    {
        return await _dbContext.SavedSearches
            .Where(s => s.LastUsedAt.HasValue)
            .OrderByDescending(s => s.LastUsedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task UpdateSavedSearchAsync(SavedSearch savedSearch)
    {
        try
        {
            savedSearch.UpdatedAt = DateTime.UtcNow;
            _dbContext.SavedSearches.Update(savedSearch);
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Saved search updated: {Id}", savedSearch.SavedSearchId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating saved search: {Id}", savedSearch.SavedSearchId);
            throw;
        }
    }

    public async Task DeleteSavedSearchAsync(string savedSearchId)
    {
        try
        {
            var savedSearch = await _dbContext.SavedSearches.FindAsync(savedSearchId);
            if (savedSearch != null)
            {
                _dbContext.SavedSearches.Remove(savedSearch);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Saved search deleted: {Id}", savedSearchId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting saved search: {Id}", savedSearchId);
            throw;
        }
    }

    public async Task MarkSearchAsUsedAsync(string savedSearchId)
    {
        try
        {
            var savedSearch = await _dbContext.SavedSearches.FindAsync(savedSearchId);
            if (savedSearch != null)
            {
                savedSearch.UseCount++;
                savedSearch.LastUsedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking search as used: {Id}", savedSearchId);
        }
    }

    public SearchFilter ConvertToFilter(SavedSearch savedSearch)
    {
        return new SearchFilter
        {
            SearchTerm = savedSearch.SearchTerm,
            Categories = DeserializeList(savedSearch.Categories),
            TagIds = DeserializeList(savedSearch.TagIds),
            PersonIds = DeserializeList(savedSearch.PersonIds),
            LocationIds = DeserializeList(savedSearch.LocationIds),
            EraIds = DeserializeList(savedSearch.EraIds),
            StartDate = savedSearch.StartDate,
            EndDate = savedSearch.EndDate,
            HasAudio = savedSearch.HasAudio,
            HasTranscript = savedSearch.HasTranscript,
            MinConfidence = savedSearch.MinConfidence,
            SortBy = savedSearch.SortBy,
            PageSize = savedSearch.PageSize
        };
    }

    private List<string> DeserializeList(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    #endregion
}
