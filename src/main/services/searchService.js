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
      const ftsQuery = this._sanitizeFtsQuery(query);
      if (ftsQuery) {
        // events_fts is an external-content FTS5 table keyed on rowid, so we
        // resolve matching event_ids via the events.rowid join.
        conditions.push(`e.event_id IN (
          SELECT e2.event_id FROM events_fts fts
          JOIN events e2 ON fts.rowid = e2.rowid
          WHERE events_fts MATCH ?
        )`);
        params.push(ftsQuery);
      }
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
      conditions.push(`e.event_id IN (
        SELECT et.event_id FROM event_tags et
        JOIN tags t ON et.tag_id = t.tag_id
        WHERE t.tag_name IN (${placeholders})
        GROUP BY et.event_id
        HAVING COUNT(DISTINCT t.tag_name) = ?
      )`);
      params.push(...tags, tags.length);
    }

    // People filter
    if (people.length > 0) {
      const placeholders = people.map(() => '?').join(',');
      conditions.push(`e.event_id IN (
        SELECT ep.event_id FROM event_people ep
        JOIN people p ON ep.person_id = p.person_id
        WHERE p.name IN (${placeholders})
      )`);
      params.push(...people);
    }

    // Location filter
    if (locations.length > 0) {
      const placeholders = locations.map(() => '?').join(',');
      conditions.push(`e.event_id IN (
        SELECT el.event_id FROM event_locations el
        JOIN locations l ON el.location_id = l.location_id
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
        conditions.push(`e.raw_transcript IS NOT NULL AND e.raw_transcript != ''`);
      } else {
        conditions.push(`(e.raw_transcript IS NULL OR e.raw_transcript = '')`);
      }
    }

    // Cross-reference filter
    if (hasCrossReferences !== null) {
      if (hasCrossReferences) {
        conditions.push(`e.event_id IN (
          SELECT DISTINCT event_id_1 FROM cross_references
          UNION
          SELECT DISTINCT event_id_2 FROM cross_references
        )`);
      } else {
        conditions.push(`e.event_id NOT IN (
          SELECT DISTINCT event_id_1 FROM cross_references
          UNION
          SELECT DISTINCT event_id_2 FROM cross_references
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

    // Build ORDER BY clause (both sort field and order are validated against
    // allowlists to prevent SQL injection).
    const sortField = this._getSortField(sortBy);
    const order = String(sortOrder).toUpperCase() === 'ASC' ? 'ASC' : 'DESC';
    const orderClause = `ORDER BY ${sortField} ${order}`;

    // Calculate pagination
    const offset = (page - 1) * pageSize;

    // Get total count
    const countSql = `
      SELECT COUNT(DISTINCT e.event_id) as total
      FROM events e
      ${whereClause}
    `;
    const countResult = this.db.prepare(countSql).get(...params);
    const totalResults = countResult.total;

    // Get paginated results with era metadata
    const sql = `
      SELECT DISTINCT e.event_id, e.title, e.start_date, e.end_date,
             e.description, e.category, e.era_id,
             er.name as era_name, er.color_code as era_color
      FROM events e
      LEFT JOIN eras er ON e.era_id = er.era_id
      ${whereClause}
      ${orderClause}
      LIMIT ? OFFSET ?
    `;
    const events = this.db.prepare(sql).all(...params, pageSize, offset);

    // Batch-load relationships for the whole page (3 queries total, not N+1)
    const eventIds = events.map(e => e.event_id);
    const relationships = this._batchLoadRelationships(eventIds);
    events.forEach(event => {
      event.tags = relationships.tags[event.event_id] || [];
      event.people = relationships.people[event.event_id] || [];
      event.locations = relationships.locations[event.event_id] || [];
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
      SELECT e.category, COUNT(DISTINCT e.event_id) as count
      FROM events e
      ${whereClause}
      GROUP BY e.category
      ORDER BY count DESC
    `;
    facets.categories = this.db.prepare(categorySql).all(...params);

    // Tag facets
    const tagSql = `
      SELECT t.tag_name as name, COUNT(DISTINCT e.event_id) as count
      FROM events e
      JOIN event_tags et ON e.event_id = et.event_id
      JOIN tags t ON et.tag_id = t.tag_id
      ${whereClause}
      GROUP BY t.tag_name
      ORDER BY count DESC
      LIMIT 50
    `;
    facets.tags = this.db.prepare(tagSql).all(...params);

    // People facets
    const peopleSql = `
      SELECT p.name, COUNT(DISTINCT e.event_id) as count
      FROM events e
      JOIN event_people ep ON e.event_id = ep.event_id
      JOIN people p ON ep.person_id = p.person_id
      ${whereClause}
      GROUP BY p.name
      ORDER BY count DESC
      LIMIT 50
    `;
    facets.people = this.db.prepare(peopleSql).all(...params);

    // Location facets
    const locationSql = `
      SELECT l.name, COUNT(DISTINCT e.event_id) as count
      FROM events e
      JOIN event_locations el ON e.event_id = el.event_id
      JOIN locations l ON el.location_id = l.location_id
      ${whereClause}
      GROUP BY l.name
      ORDER BY count DESC
      LIMIT 50
    `;
    facets.locations = this.db.prepare(locationSql).all(...params);

    // Era facets
    const eraSql = `
      SELECT er.era_id as id, er.name, er.color_code as color, COUNT(DISTINCT e.event_id) as count
      FROM events e
      LEFT JOIN eras er ON e.era_id = er.era_id
      ${whereClause}
      GROUP BY er.era_id, er.name, er.color_code
      ORDER BY count DESC
    `;
    facets.eras = this.db.prepare(eraSql).all(...params);

    // Date range facets (by year)
    const dateSql = `
      SELECT
        strftime('%Y', e.start_date) as year,
        COUNT(DISTINCT e.event_id) as count
      FROM events e
      ${whereClause}
      GROUP BY year
      ORDER BY year DESC
    `;
    facets.years = this.db.prepare(dateSql).all(...params);

    return facets;
  }

  /**
   * Sanitize a user-supplied query for FTS5 MATCH.
   * Splits input into tokens and wraps each as a quoted phrase so that
   * FTS5 special characters (", *, (), :, ^, -) in ordinary input do not
   * throw fts5 syntax errors. Returns null if nothing usable remains.
   */
  _sanitizeFtsQuery(query) {
    if (!query || typeof query !== 'string') {
      return null;
    }
    const tokens = query.trim().split(/\s+/).filter(Boolean);
    if (tokens.length === 0) {
      return null;
    }
    // Escape embedded double quotes by doubling them, then wrap each token
    // as a phrase. Tokens are implicitly AND-ed by FTS5.
    return tokens
      .map(token => `"${token.replace(/"/g, '""')}"`)
      .join(' ');
  }

  /**
   * Batch-load relationships (tags, people, locations) for a page of events.
   * Uses 3 queries total instead of 3 queries per event.
   * @param {Array<string>} eventIds
   * @returns {{tags: Object, people: Object, locations: Object}} maps of
   *          event_id -> string[]
   */
  _batchLoadRelationships(eventIds) {
    const relationships = { tags: {}, people: {}, locations: {} };
    if (!eventIds || eventIds.length === 0) {
      return relationships;
    }

    const placeholders = eventIds.map(() => '?').join(',');

    const tagRows = this.db.prepare(`
      SELECT et.event_id, t.tag_name
      FROM event_tags et
      JOIN tags t ON et.tag_id = t.tag_id
      WHERE et.event_id IN (${placeholders})
    `).all(...eventIds);
    tagRows.forEach(row => {
      (relationships.tags[row.event_id] ||= []).push(row.tag_name);
    });

    const peopleRows = this.db.prepare(`
      SELECT ep.event_id, p.name
      FROM event_people ep
      JOIN people p ON ep.person_id = p.person_id
      WHERE ep.event_id IN (${placeholders})
    `).all(...eventIds);
    peopleRows.forEach(row => {
      (relationships.people[row.event_id] ||= []).push(row.name);
    });

    const locationRows = this.db.prepare(`
      SELECT el.event_id, l.name
      FROM event_locations el
      JOIN locations l ON el.location_id = l.location_id
      WHERE el.event_id IN (${placeholders})
    `).all(...eventIds);
    locationRows.forEach(row => {
      (relationships.locations[row.event_id] ||= []).push(row.name);
    });

    return relationships;
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
      'relevance': 'e.start_date' // FTS rank isn't selected here; fall back to date
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
      SELECT DISTINCT tag_name as title, 'tag' as type
      FROM tags
      WHERE tag_name LIKE ?
      ORDER BY tag_name
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
      SELECT setting_key, setting_value, updated_at
      FROM app_settings
      WHERE setting_key LIKE 'saved_search_%'
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
      INSERT INTO app_settings (setting_key, setting_value)
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
      DELETE FROM app_settings WHERE setting_key = ?
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
        SELECT t.tag_name as name, COUNT(*) as usage_count
        FROM tags t
        JOIN event_tags et ON t.tag_id = et.tag_id
        GROUP BY t.tag_id
        ORDER BY usage_count DESC
        LIMIT 10
      `).all(),
      mostReferencedPeople: this.db.prepare(`
        SELECT p.name, COUNT(*) as reference_count
        FROM people p
        JOIN event_people ep ON p.person_id = ep.person_id
        GROUP BY p.person_id
        ORDER BY reference_count DESC
        LIMIT 10
      `).all()
    };
  }
}

module.exports = SearchService;
