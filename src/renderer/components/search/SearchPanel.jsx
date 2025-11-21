import React, { useState, useEffect, useCallback } from 'react';
import './SearchPanel.css';

const SearchPanel = ({ onSearch, onClose }) => {
  const [query, setQuery] = useState('');
  const [suggestions, setSuggestions] = useState([]);
  const [facets, setFacets] = useState(null);
  const [results, setResults] = useState([]);
  const [pagination, setPagination] = useState(null);
  const [loading, setLoading] = useState(false);

  // Filter states
  const [selectedCategories, setSelectedCategories] = useState([]);
  const [selectedTags, setSelectedTags] = useState([]);
  const [selectedPeople, setSelectedPeople] = useState([]);
  const [selectedLocations, setSelectedLocations] = useState([]);
  const [selectedEra, setSelectedEra] = useState(null);
  const [dateRange, setDateRange] = useState({ start: '', end: '' });
  const [hasTranscript, setHasTranscript] = useState(null);
  const [sortBy, setSortBy] = useState('date');
  const [sortOrder, setSortOrder] = useState('desc');
  const [currentPage, setCurrentPage] = useState(1);

  // Debounce suggestions
  useEffect(() => {
    if (query.length < 2) {
      setSuggestions([]);
      return;
    }

    const timer = setTimeout(async () => {
      const sugg = await window.electronAPI.search.getSuggestions(query);
      setSuggestions(sugg);
    }, 300);

    return () => clearTimeout(timer);
  }, [query]);

  const performSearch = useCallback(async (page = 1) => {
    setLoading(true);
    setCurrentPage(page);

    const searchOptions = {
      query,
      categories: selectedCategories,
      tags: selectedTags,
      people: selectedPeople,
      locations: selectedLocations,
      eraId: selectedEra,
      startDate: dateRange.start || null,
      endDate: dateRange.end || null,
      hasTranscript,
      sortBy,
      sortOrder,
      page,
      pageSize: 20
    };

    try {
      const result = await window.electronAPI.search.search(searchOptions);
      setResults(result.events);
      setPagination(result.pagination);
      setFacets(result.facets);

      if (onSearch) {
        onSearch(result);
      }
    } catch (error) {
      console.error('Search error:', error);
    } finally {
      setLoading(false);
    }
  }, [query, selectedCategories, selectedTags, selectedPeople, selectedLocations,
      selectedEra, dateRange, hasTranscript, sortBy, sortOrder, onSearch]);

  const handleSearch = (e) => {
    e.preventDefault();
    performSearch(1);
  };

  const handleFilterChange = (filterType, value, checked) => {
    let newFilters;
    switch (filterType) {
      case 'category':
        newFilters = checked
          ? [...selectedCategories, value]
          : selectedCategories.filter(c => c !== value);
        setSelectedCategories(newFilters);
        break;
      case 'tag':
        newFilters = checked
          ? [...selectedTags, value]
          : selectedTags.filter(t => t !== value);
        setSelectedTags(newFilters);
        break;
      case 'person':
        newFilters = checked
          ? [...selectedPeople, value]
          : selectedPeople.filter(p => p !== value);
        setSelectedPeople(newFilters);
        break;
      case 'location':
        newFilters = checked
          ? [...selectedLocations, value]
          : selectedLocations.filter(l => l !== value);
        setSelectedLocations(newFilters);
        break;
    }
  };

  const clearFilters = () => {
    setQuery('');
    setSelectedCategories([]);
    setSelectedTags([]);
    setSelectedPeople([]);
    setSelectedLocations([]);
    setSelectedEra(null);
    setDateRange({ start: '', end: '' });
    setHasTranscript(null);
    setResults([]);
    setFacets(null);
    setPagination(null);
  };

  const handleEventClick = async (eventId) => {
    // Emit event to parent or use global state to open event details
    if (window.electronAPI.events) {
      const event = await window.electronAPI.events.getById(eventId);
      // Trigger event details modal (implementation depends on your app structure)
      console.log('Open event:', event);
    }
  };

  const saveCurrentSearch = async () => {
    const name = prompt('Enter a name for this search:');
    if (name) {
      await window.electronAPI.search.saveSearch(name, {
        query,
        categories: selectedCategories,
        tags: selectedTags,
        people: selectedPeople,
        locations: selectedLocations,
        eraId: selectedEra,
        startDate: dateRange.start || null,
        endDate: dateRange.end || null,
        hasTranscript,
        sortBy,
        sortOrder
      });
      alert('Search saved!');
    }
  };

  return (
    <div className="search-panel">
      <div className="search-header">
        <h2>Advanced Search</h2>
        {onClose && (
          <button className="close-btn" onClick={onClose}>×</button>
        )}
      </div>

      <form onSubmit={handleSearch} className="search-form">
        <div className="search-input-group">
          <input
            type="text"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Search events, tags, people, locations..."
            className="search-input"
          />
          <button type="submit" className="search-btn" disabled={loading}>
            {loading ? 'Searching...' : 'Search'}
          </button>
        </div>

        {suggestions.length > 0 && (
          <div className="suggestions-dropdown">
            {suggestions.map((sugg, idx) => (
              <div
                key={idx}
                className={`suggestion-item ${sugg.type}`}
                onClick={() => {
                  setQuery(sugg.title);
                  setSuggestions([]);
                }}
              >
                <span className="suggestion-type">{sugg.type}</span>
                <span className="suggestion-title">{sugg.title}</span>
              </div>
            ))}
          </div>
        )}
      </form>

      <div className="search-content">
        <div className="search-sidebar">
          <div className="filters-section">
            <div className="filter-actions">
              <button onClick={clearFilters} className="clear-filters-btn">
                Clear All Filters
              </button>
              <button onClick={saveCurrentSearch} className="save-search-btn">
                Save Search
              </button>
            </div>

            {/* Date Range Filter */}
            <div className="filter-group">
              <h3>Date Range</h3>
              <div className="date-inputs">
                <input
                  type="date"
                  value={dateRange.start}
                  onChange={(e) => setDateRange({ ...dateRange, start: e.target.value })}
                  placeholder="Start date"
                />
                <input
                  type="date"
                  value={dateRange.end}
                  onChange={(e) => setDateRange({ ...dateRange, end: e.target.value })}
                  placeholder="End date"
                />
              </div>
            </div>

            {/* Category Facets */}
            {facets?.categories && facets.categories.length > 0 && (
              <div className="filter-group">
                <h3>Categories</h3>
                <div className="facet-list">
                  {facets.categories.map((cat) => (
                    <label key={cat.category} className="facet-item">
                      <input
                        type="checkbox"
                        checked={selectedCategories.includes(cat.category)}
                        onChange={(e) => handleFilterChange('category', cat.category, e.target.checked)}
                      />
                      <span className="facet-name">{cat.category}</span>
                      <span className="facet-count">{cat.count}</span>
                    </label>
                  ))}
                </div>
              </div>
            )}

            {/* Tag Facets */}
            {facets?.tags && facets.tags.length > 0 && (
              <div className="filter-group">
                <h3>Tags</h3>
                <div className="facet-list">
                  {facets.tags.slice(0, 15).map((tag) => (
                    <label key={tag.name} className="facet-item">
                      <input
                        type="checkbox"
                        checked={selectedTags.includes(tag.name)}
                        onChange={(e) => handleFilterChange('tag', tag.name, e.target.checked)}
                      />
                      <span className="facet-name">{tag.name}</span>
                      <span className="facet-count">{tag.count}</span>
                    </label>
                  ))}
                </div>
              </div>
            )}

            {/* People Facets */}
            {facets?.people && facets.people.length > 0 && (
              <div className="filter-group">
                <h3>People</h3>
                <div className="facet-list">
                  {facets.people.slice(0, 15).map((person) => (
                    <label key={person.name} className="facet-item">
                      <input
                        type="checkbox"
                        checked={selectedPeople.includes(person.name)}
                        onChange={(e) => handleFilterChange('person', person.name, e.target.checked)}
                      />
                      <span className="facet-name">{person.name}</span>
                      <span className="facet-count">{person.count}</span>
                    </label>
                  ))}
                </div>
              </div>
            )}

            {/* Location Facets */}
            {facets?.locations && facets.locations.length > 0 && (
              <div className="filter-group">
                <h3>Locations</h3>
                <div className="facet-list">
                  {facets.locations.slice(0, 15).map((loc) => (
                    <label key={loc.name} className="facet-item">
                      <input
                        type="checkbox"
                        checked={selectedLocations.includes(loc.name)}
                        onChange={(e) => handleFilterChange('location', loc.name, e.target.checked)}
                      />
                      <span className="facet-name">{loc.name}</span>
                      <span className="facet-count">{loc.count}</span>
                    </label>
                  ))}
                </div>
              </div>
            )}

            {/* Transcript Filter */}
            <div className="filter-group">
              <h3>Transcript</h3>
              <label className="facet-item">
                <input
                  type="checkbox"
                  checked={hasTranscript === true}
                  onChange={(e) => setHasTranscript(e.target.checked ? true : null)}
                />
                <span className="facet-name">Has Transcript</span>
              </label>
            </div>

            {/* Sort Options */}
            <div className="filter-group">
              <h3>Sort By</h3>
              <select value={sortBy} onChange={(e) => setSortBy(e.target.value)} className="sort-select">
                <option value="date">Date</option>
                <option value="title">Title</option>
                <option value="category">Category</option>
                <option value="duration">Duration</option>
              </select>
              <select value={sortOrder} onChange={(e) => setSortOrder(e.target.value)} className="sort-select">
                <option value="desc">Descending</option>
                <option value="asc">Ascending</option>
              </select>
            </div>
          </div>
        </div>

        <div className="search-results">
          {loading && <div className="loading">Searching...</div>}

          {!loading && results.length === 0 && query && (
            <div className="no-results">
              <p>No results found for "{query}"</p>
              <p>Try adjusting your filters or search terms.</p>
            </div>
          )}

          {!loading && results.length > 0 && (
            <>
              <div className="results-header">
                <h3>
                  {pagination?.totalResults} result{pagination?.totalResults !== 1 ? 's' : ''}
                </h3>
              </div>

              <div className="results-list">
                {results.map((event) => (
                  <div
                    key={event.id}
                    className="result-item"
                    onClick={() => handleEventClick(event.id)}
                  >
                    <div className="result-header">
                      <h4>{event.title}</h4>
                      <span className="result-category">{event.category}</span>
                    </div>
                    <div className="result-date">
                      {new Date(event.start_date).toLocaleDateString()}
                    </div>
                    {event.description && (
                      <p className="result-description">{event.description}</p>
                    )}
                    <div className="result-meta">
                      {event.tags && event.tags.length > 0 && (
                        <div className="result-tags">
                          {event.tags.map((tag) => (
                            <span key={tag.id} className="result-tag" style={{ backgroundColor: tag.color }}>
                              {tag.name}
                            </span>
                          ))}
                        </div>
                      )}
                      {event.people && event.people.length > 0 && (
                        <div className="result-people">
                          People: {event.people.map(p => p.name).join(', ')}
                        </div>
                      )}
                      {event.locations && event.locations.length > 0 && (
                        <div className="result-locations">
                          Locations: {event.locations.map(l => l.name).join(', ')}
                        </div>
                      )}
                    </div>
                  </div>
                ))}
              </div>

              {pagination && pagination.totalPages > 1 && (
                <div className="pagination">
                  <button
                    onClick={() => performSearch(currentPage - 1)}
                    disabled={!pagination.hasPreviousPage}
                    className="pagination-btn"
                  >
                    Previous
                  </button>
                  <span className="pagination-info">
                    Page {pagination.page} of {pagination.totalPages}
                  </span>
                  <button
                    onClick={() => performSearch(currentPage + 1)}
                    disabled={!pagination.hasNextPage}
                    className="pagination-btn"
                  >
                    Next
                  </button>
                </div>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  );
};

export default SearchPanel;
