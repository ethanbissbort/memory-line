/**
 * Enhanced Search Service with Faceted Filtering
 * Provides advanced search capabilities with multi-dimensional filtering
 */

class SearchService {
  constructor(db) {
    this.db = db;
  }

  /**
   * Perform faceted search with multiple filter dimensions
   * @param {Object} options - Search options
   * @param {string} options.query - Full-text search query
   * @param {Array<string>} options.categories - Filter by categories
   * @param {Array<string>} options.tags - Filter by tag names
   * @param {Array<string>} options.people - Filter by person names
   * @param {Array<string>} options.locations - Filter by location names
   * @param {string} options.startDate - Filter events after this date (ISO format)
   * @param {string} options.endDate - Filter events before this date (ISO format)
   * @param {string} options.eraId - Filter by era ID
   * @param {boolean} options.hasTranscript - Filter events with/without transcripts
   * @param {boolean} options.hasCrossReferences - Filter events with/without cross-references
   * @param {number} options.minDuration - Minimum duration in seconds
   * @param {number} options.maxDuration - Maximum duration in seconds
   * @param {number} options.page - Page number (default: 1)
   * @param {number} options.pageSize - Results per page (default: 50)
   * @param {string} options.sortBy - Sort field (date, title, category, duration)
   * @param {string} options.sortOrder - Sort order (asc, desc)
   * @returns {Object} Search results with facets
   */
  search(options = {}) {
    const {
      query = '',
      categories = [],
      tags = [],
      people = [],
      locations = [],
      startDate = null,
      endDate = null,
      eraId = null,
      hasTranscript = null,
      hasCrossReferences = null,
      minDuration = null,
      maxDuration = null,
      page = 1,
      pageSize = 50,
      sortBy = 'date',
      sortOrder = 'desc'
    } = options;

    // Build the WHERE clause dynamically
    const conditions = [];
    const params = [];

    // Full-text search if query provided
    if (query && query.trim()) {
      conditions.push(`e.id IN (
        SELECT event_id FROM event_fts
        WHERE event_fts MATCH ?
      )`);
      params.push(query.trim());
    }

    // Category filter
    if (categories.length > 0) {
      const placeholders = categories.map(() => '?').join(',');
      conditions.push(`e.category IN (${placeholders})`);
      params.push(...categories);
    }

    // Tag filter
    if (tags.length > 0) {
      const placeholders = tags.map(() => '?').join(',');
      conditions.push(`e.id IN (
        SELECT et.event_id FROM event_tags et
        JOIN tags t ON et.tag_id = t.id
        WHERE t.name IN (${placeholders})
        GROUP BY et.event_id
        HAVING COUNT(DISTINCT t.name) = ?
      )`);
      params.push(...tags, tags.length);
    }

    // People filter
    if (people.length > 0) {
      const placeholders = people.map(() => '?').join(',');
      conditions.push(`e.id IN (
        SELECT ep.event_id FROM event_people ep
        JOIN people p ON ep.person_id = p.id
        WHERE p.name IN (${placeholders})
      )`);
      params.push(...people);
    }

    // Location filter
    if (locations.length > 0) {
      const placeholders = locations.map(() => '?').join(',');
      conditions.push(`e.id IN (
        SELECT el.event_id FROM event_locations el
        JOIN locations l ON el.location_id = l.id
        WHERE l.name IN (${placeholders})
      )`);
      params.push(...locations);
    }

    // Date range filter
    if (startDate) {
      conditions.push('e.start_date >= ?');
      params.push(startDate);
    }
    if (endDate) {
      conditions.push('e.start_date <= ?');
      params.push(endDate);
    }

    // Era filter
    if (eraId) {
      conditions.push('e.era_id = ?');
      params.push(eraId);
    }

    // Transcript filter
    if (hasTranscript !== null) {
      if (hasTranscript) {
        conditions.push(`e.transcript IS NOT NULL AND e.transcript != ''`);
      } else {
        conditions.push(`(e.transcript IS NULL OR e.transcript = '')`);
      }
    }

    // Cross-reference filter
    if (hasCrossReferences !== null) {
      if (hasCrossReferences) {
        conditions.push(`e.id IN (
          SELECT DISTINCT source_event_id FROM cross_references
          UNION
          SELECT DISTINCT target_event_id FROM cross_references
        )`);
      } else {
        conditions.push(`e.id NOT IN (
          SELECT DISTINCT source_event_id FROM cross_references
          UNION
          SELECT DISTINCT target_event_id FROM cross_references
        )`);
      }
    }

    // Duration filter
    if (minDuration !== null || maxDuration !== null) {
      conditions.push(`(e.end_date IS NOT NULL AND e.start_date IS NOT NULL)`);

      if (minDuration !== null) {
        conditions.push(`(julianday(e.end_date) - julianday(e.start_date)) * 86400 >= ?`);
        params.push(minDuration);
      }
      if (maxDuration !== null) {
        conditions.push(`(julianday(e.end_date) - julianday(e.start_date)) * 86400 <= ?`);
        params.push(maxDuration);
      }
    }

    // Build the WHERE clause
    const whereClause = conditions.length > 0 ? `WHERE ${conditions.join(' AND ')}` : '';

    // Build ORDER BY clause
    const sortField = this._getSortField(sortBy);
    const orderClause = `ORDER BY ${sortField} ${sortOrder.toUpperCase()}`;

    // Calculate pagination
    const offset = (page - 1) * pageSize;

    // Get total count
    const countSql = `
      SELECT COUNT(DISTINCT e.id) as total
      FROM events e
      ${whereClause}
    `;
    const countResult = this.db.prepare(countSql).get(...params);
    const totalResults = countResult.total;

    // Get paginated results
    const sql = `
      SELECT DISTINCT e.*
      FROM events e
      ${whereClause}
      ${orderClause}
      LIMIT ? OFFSET ?
    `;
    const events = this.db.prepare(sql).all(...params, pageSize, offset);

    // Load relationships for each event
    events.forEach(event => {
      this._loadEventRelationships(event);
    });

    // Calculate facets (available filter options based on current results)
    const facets = this._calculateFacets(whereClause, params);

    return {
      events,
      pagination: {
        page,
        pageSize,
        totalResults,
        totalPages: Math.ceil(totalResults / pageSize),
        hasNextPage: page < Math.ceil(totalResults / pageSize),
        hasPreviousPage: page > 1
      },
      facets,
      appliedFilters: {
        query,
        categories,
        tags,
        people,
        locations,
        startDate,
        endDate,
        eraId,
        hasTranscript,
        hasCrossReferences,
        minDuration,
        maxDuration
      }
    };
  }

  /**
   * Get available facet values for filtering
   */
  _calculateFacets(whereClause, params) {
    const facets = {};

    // Category facets
    const categorySql = `
      SELECT e.category, COUNT(DISTINCT e.id) as count
      FROM events e
      ${whereClause}
      GROUP BY e.category
      ORDER BY count DESC
    `;
    facets.categories = this.db.prepare(categorySql).all(...params);

    // Tag facets
    const tagSql = `
      SELECT t.name, COUNT(DISTINCT e.id) as count
      FROM events e
      JOIN event_tags et ON e.id = et.event_id
      JOIN tags t ON et.tag_id = t.id
      ${whereClause}
      GROUP BY t.name
      ORDER BY count DESC
      LIMIT 50
    `;
    facets.tags = this.db.prepare(tagSql).all(...params);

    // People facets
    const peopleSql = `
      SELECT p.name, COUNT(DISTINCT e.id) as count
      FROM events e
      JOIN event_people ep ON e.id = ep.event_id
      JOIN people p ON ep.person_id = p.id
      ${whereClause}
      GROUP BY p.name
      ORDER BY count DESC
      LIMIT 50
    `;
    facets.people = this.db.prepare(peopleSql).all(...params);

    // Location facets
    const locationSql = `
      SELECT l.name, COUNT(DISTINCT e.id) as count
      FROM events e
      JOIN event_locations el ON e.id = el.event_id
      JOIN locations l ON el.location_id = l.id
      ${whereClause}
      GROUP BY l.name
      ORDER BY count DESC
      LIMIT 50
    `;
    facets.locations = this.db.prepare(locationSql).all(...params);

    // Era facets
    const eraSql = `
      SELECT er.id, er.name, er.color, COUNT(DISTINCT e.id) as count
      FROM events e
      LEFT JOIN eras er ON e.era_id = er.id
      ${whereClause}
      GROUP BY er.id, er.name, er.color
      ORDER BY count DESC
    `;
    facets.eras = this.db.prepare(eraSql).all(...params);

    // Date range facets (by year)
    const dateSql = `
      SELECT
        strftime('%Y', e.start_date) as year,
        COUNT(DISTINCT e.id) as count
      FROM events e
      ${whereClause}
      GROUP BY year
      ORDER BY year DESC
    `;
    facets.years = this.db.prepare(dateSql).all(...params);

    return facets;
  }

  /**
   * Load relationships (tags, people, locations) for an event
   */
  _loadEventRelationships(event) {
    // Load tags
    const tags = this.db.prepare(`
      SELECT t.* FROM tags t
      JOIN event_tags et ON t.id = et.tag_id
      WHERE et.event_id = ?
    `).all(event.id);
    event.tags = tags;

    // Load people
    const people = this.db.prepare(`
      SELECT p.* FROM people p
      JOIN event_people ep ON p.id = ep.person_id
      WHERE ep.event_id = ?
    `).all(event.id);
    event.people = people;

    // Load locations
    const locations = this.db.prepare(`
      SELECT l.* FROM locations l
      JOIN event_locations el ON l.id = el.location_id
      WHERE el.event_id = ?
    `).all(event.id);
    event.locations = locations;

    return event;
  }

  /**
   * Get sort field for SQL query
   */
  _getSortField(sortBy) {
    const sortFields = {
      'date': 'e.start_date',
      'title': 'e.title',
      'category': 'e.category',
      'duration': '(julianday(e.end_date) - julianday(e.start_date))',
      'relevance': 'e.id' // For FTS, we'd use rank() but default to id
    };
    return sortFields[sortBy] || sortFields.date;
  }

  /**
   * Get search suggestions based on partial query
   * @param {string} query - Partial search query
   * @param {number} limit - Maximum number of suggestions
   * @returns {Array} Search suggestions
   */
  getSuggestions(query, limit = 10) {
    if (!query || query.trim().length < 2) {
      return [];
    }

    const suggestions = [];

    // Event title suggestions
    const titleSuggestions = this.db.prepare(`
      SELECT DISTINCT title, 'event' as type
      FROM events
      WHERE title LIKE ?
      ORDER BY start_date DESC
      LIMIT ?
    `).all(`%${query}%`, limit);
    suggestions.push(...titleSuggestions);

    // Tag suggestions
    const tagSuggestions = this.db.prepare(`
      SELECT DISTINCT name as title, 'tag' as type
      FROM tags
      WHERE name LIKE ?
      ORDER BY name
      LIMIT ?
    `).all(`%${query}%`, limit);
    suggestions.push(...tagSuggestions);

    // People suggestions
    const peopleSuggestions = this.db.prepare(`
      SELECT DISTINCT name as title, 'person' as type
      FROM people
      WHERE name LIKE ?
      ORDER BY name
      LIMIT ?
    `).all(`%${query}%`, limit);
    suggestions.push(...peopleSuggestions);

    // Location suggestions
    const locationSuggestions = this.db.prepare(`
      SELECT DISTINCT name as title, 'location' as type
      FROM locations
      WHERE name LIKE ?
      ORDER BY name
      LIMIT ?
    `).all(`%${query}%`, limit);
    suggestions.push(...locationSuggestions);

    return suggestions.slice(0, limit);
  }

  /**
   * Get saved searches for the user
   * @returns {Array} Saved searches
   */
  getSavedSearches() {
    return this.db.prepare(`
      SELECT * FROM app_settings
      WHERE key LIKE 'saved_search_%'
      ORDER BY updated_at DESC
    `).all();
  }

  /**
   * Save a search query for later use
   * @param {string} name - Name for the saved search
   * @param {Object} searchOptions - Search options to save
   * @returns {Object} Success result
   */
  saveSearch(name, searchOptions) {
    const key = `saved_search_${Date.now()}`;
    const value = JSON.stringify({
      name,
      options: searchOptions,
      createdAt: new Date().toISOString()
    });

    this.db.prepare(`
      INSERT INTO app_settings (key, value)
      VALUES (?, ?)
    `).run(key, value);

    return { success: true, key };
  }

  /**
   * Delete a saved search
   * @param {string} key - The saved search key
   */
  deleteSavedSearch(key) {
    this.db.prepare(`
      DELETE FROM app_settings WHERE key = ?
    `).run(key);

    return { success: true };
  }

  /**
   * Get search analytics (most searched terms, etc.)
   * @returns {Object} Search analytics
   */
  getSearchAnalytics() {
    // This would track search queries in a separate table
    // For now, return basic statistics
    return {
      totalEvents: this.db.prepare('SELECT COUNT(*) as count FROM events').get().count,
      totalTags: this.db.prepare('SELECT COUNT(*) as count FROM tags').get().count,
      totalPeople: this.db.prepare('SELECT COUNT(*) as count FROM people').get().count,
      totalLocations: this.db.prepare('SELECT COUNT(*) as count FROM locations').get().count,
      mostUsedTags: this.db.prepare(`
        SELECT t.name, COUNT(*) as usage_count
        FROM tags t
        JOIN event_tags et ON t.id = et.tag_id
        GROUP BY t.id
        ORDER BY usage_count DESC
        LIMIT 10
      `).all(),
      mostReferencedPeople: this.db.prepare(`
        SELECT p.name, COUNT(*) as reference_count
        FROM people p
        JOIN event_people ep ON p.id = ep.person_id
        GROUP BY p.id
        ORDER BY reference_count DESC
        LIMIT 10
      `).all()
    };
  }
}

module.exports = SearchService;
