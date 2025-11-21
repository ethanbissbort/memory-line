/**
 * Unit tests for SearchService
 */

const SearchService = require('../../src/main/services/searchService');
const Database = require('better-sqlite3');

describe('SearchService', () => {
  let db;
  let searchService;

  beforeEach(() => {
    // Create in-memory database
    db = new Database(':memory:');

    // Create schema
    db.exec(`
      CREATE TABLE events (
        id TEXT PRIMARY KEY,
        title TEXT NOT NULL,
        description TEXT,
        start_date TEXT NOT NULL,
        end_date TEXT,
        category TEXT,
        era_id TEXT,
        transcript TEXT,
        created_at TEXT DEFAULT CURRENT_TIMESTAMP
      );

      CREATE TABLE eras (
        id TEXT PRIMARY KEY,
        name TEXT NOT NULL,
        color TEXT,
        start_date TEXT,
        end_date TEXT
      );

      CREATE TABLE tags (
        id TEXT PRIMARY KEY,
        name TEXT UNIQUE NOT NULL,
        color TEXT
      );

      CREATE TABLE people (
        id TEXT PRIMARY KEY,
        name TEXT UNIQUE NOT NULL
      );

      CREATE TABLE locations (
        id TEXT PRIMARY KEY,
        name TEXT NOT NULL,
        latitude REAL,
        longitude REAL
      );

      CREATE TABLE event_tags (
        event_id TEXT,
        tag_id TEXT,
        PRIMARY KEY (event_id, tag_id)
      );

      CREATE TABLE event_people (
        event_id TEXT,
        person_id TEXT,
        PRIMARY KEY (event_id, person_id)
      );

      CREATE TABLE event_locations (
        event_id TEXT,
        location_id TEXT,
        PRIMARY KEY (event_id, location_id)
      );

      CREATE TABLE cross_references (
        id TEXT PRIMARY KEY,
        source_event_id TEXT,
        target_event_id TEXT,
        relationship_type TEXT,
        confidence REAL
      );

      CREATE TABLE app_settings (
        key TEXT PRIMARY KEY,
        value TEXT,
        updated_at TEXT DEFAULT CURRENT_TIMESTAMP
      );

      CREATE VIRTUAL TABLE event_fts USING fts5(event_id, content);
    `);

    // Insert test data
    const era1 = 'era-1';
    db.prepare('INSERT INTO eras (id, name, color) VALUES (?, ?, ?)').run(
      era1, 'Test Era', '#ff0000'
    );

    const event1 = 'event-1';
    const event2 = 'event-2';
    const event3 = 'event-3';

    db.prepare(`
      INSERT INTO events (id, title, description, start_date, category, era_id, transcript)
      VALUES (?, ?, ?, ?, ?, ?, ?)
    `).run(event1, 'First Event', 'Description 1', '2024-01-01', 'Personal', era1, 'Transcript 1');

    db.prepare(`
      INSERT INTO events (id, title, description, start_date, category, transcript)
      VALUES (?, ?, ?, ?, ?, ?)
    `).run(event2, 'Second Event', 'Description 2', '2024-01-15', 'Work', 'Transcript 2');

    db.prepare(`
      INSERT INTO events (id, title, description, start_date, category)
      VALUES (?, ?, ?, ?, ?)
    `).run(event3, 'Third Event', 'Description 3', '2024-02-01', 'Personal');

    // Add tags
    const tag1 = 'tag-1';
    const tag2 = 'tag-2';
    db.prepare('INSERT INTO tags (id, name, color) VALUES (?, ?, ?)').run(tag1, 'important', '#blue');
    db.prepare('INSERT INTO tags (id, name, color) VALUES (?, ?, ?)').run(tag2, 'work', '#green');

    db.prepare('INSERT INTO event_tags (event_id, tag_id) VALUES (?, ?)').run(event1, tag1);
    db.prepare('INSERT INTO event_tags (event_id, tag_id) VALUES (?, ?)').run(event2, tag2);

    // Add people
    const person1 = 'person-1';
    db.prepare('INSERT INTO people (id, name) VALUES (?, ?)').run(person1, 'John Doe');
    db.prepare('INSERT INTO event_people (event_id, person_id) VALUES (?, ?)').run(event1, person1);

    // Add locations
    const loc1 = 'loc-1';
    db.prepare('INSERT INTO locations (id, name) VALUES (?, ?)').run(loc1, 'New York');
    db.prepare('INSERT INTO event_locations (event_id, location_id) VALUES (?, ?)').run(event1, loc1);

    // Add FTS data
    db.prepare('INSERT INTO event_fts (event_id, content) VALUES (?, ?)').run(
      event1, 'First Event Description 1 Transcript 1'
    );
    db.prepare('INSERT INTO event_fts (event_id, content) VALUES (?, ?)').run(
      event2, 'Second Event Description 2 Transcript 2'
    );

    searchService = new SearchService(db);
  });

  afterEach(() => {
    db.close();
  });

  describe('search', () => {
    test('should return all events with default options', () => {
      const result = searchService.search({});

      expect(result.events).toBeDefined();
      expect(result.events.length).toBe(3);
      expect(result.pagination).toBeDefined();
      expect(result.pagination.totalResults).toBe(3);
      expect(result.facets).toBeDefined();
    });

    test('should filter by category', () => {
      const result = searchService.search({
        categories: ['Personal']
      });

      expect(result.events.length).toBe(2);
      expect(result.events.every(e => e.category === 'Personal')).toBe(true);
    });

    test('should filter by date range', () => {
      const result = searchService.search({
        startDate: '2024-01-01',
        endDate: '2024-01-20'
      });

      expect(result.events.length).toBe(2);
    });

    test('should filter by tag', () => {
      const result = searchService.search({
        tags: ['important']
      });

      expect(result.events.length).toBe(1);
      expect(result.events[0].id).toBe('event-1');
    });

    test('should filter events with transcripts', () => {
      const result = searchService.search({
        hasTranscript: true
      });

      expect(result.events.length).toBe(2);
      expect(result.events.every(e => e.transcript !== null && e.transcript !== '')).toBe(true);
    });

    test('should paginate results', () => {
      const page1 = searchService.search({ page: 1, pageSize: 2 });
      expect(page1.events.length).toBe(2);
      expect(page1.pagination.hasNextPage).toBe(true);

      const page2 = searchService.search({ page: 2, pageSize: 2 });
      expect(page2.events.length).toBe(1);
      expect(page2.pagination.hasNextPage).toBe(false);
    });

    test('should return facets', () => {
      const result = searchService.search({});

      expect(result.facets.categories).toBeDefined();
      expect(result.facets.categories.length).toBeGreaterThan(0);
      expect(result.facets.tags).toBeDefined();
      expect(result.facets.eras).toBeDefined();
    });

    test('should load event relationships', () => {
      const result = searchService.search({});
      const event = result.events.find(e => e.id === 'event-1');

      expect(event.tags).toBeDefined();
      expect(event.tags.length).toBe(1);
      expect(event.people).toBeDefined();
      expect(event.people.length).toBe(1);
      expect(event.locations).toBeDefined();
      expect(event.locations.length).toBe(1);
    });

    test('should sort by different fields', () => {
      const byDate = searchService.search({ sortBy: 'date', sortOrder: 'asc' });
      expect(byDate.events[0].start_date).toBe('2024-01-01');

      const byTitle = searchService.search({ sortBy: 'title', sortOrder: 'asc' });
      expect(byTitle.events[0].title).toBe('First Event');
    });
  });

  describe('getSuggestions', () => {
    test('should return suggestions for partial query', () => {
      const suggestions = searchService.getSuggestions('First');

      expect(suggestions).toBeDefined();
      expect(suggestions.length).toBeGreaterThan(0);
      expect(suggestions.some(s => s.title.includes('First'))).toBe(true);
    });

    test('should return empty array for short query', () => {
      const suggestions = searchService.getSuggestions('a');
      expect(suggestions).toEqual([]);
    });

    test('should limit suggestion count', () => {
      const suggestions = searchService.getSuggestions('Event', 2);
      expect(suggestions.length).toBeLessThanOrEqual(2);
    });

    test('should include different entity types', () => {
      const suggestions = searchService.getSuggestions('o');
      const types = new Set(suggestions.map(s => s.type));

      expect(types.size).toBeGreaterThan(0);
    });
  });

  describe('saveSearch', () => {
    test('should save a search', () => {
      const result = searchService.saveSearch('My Search', {
        categories: ['Personal'],
        startDate: '2024-01-01'
      });

      expect(result.success).toBe(true);
      expect(result.key).toBeDefined();
      expect(result.key).toMatch(/^saved_search_/);
    });
  });

  describe('getSavedSearches', () => {
    test('should return saved searches', () => {
      searchService.saveSearch('Search 1', { categories: ['Personal'] });
      searchService.saveSearch('Search 2', { tags: ['important'] });

      const searches = searchService.getSavedSearches();
      expect(searches.length).toBeGreaterThanOrEqual(2);
    });
  });

  describe('deleteSavedSearch', () => {
    test('should delete a saved search', () => {
      const saveResult = searchService.saveSearch('Test Search', {});
      const deleteResult = searchService.deleteSavedSearch(saveResult.key);

      expect(deleteResult.success).toBe(true);

      const searches = searchService.getSavedSearches();
      expect(searches.find(s => s.key === saveResult.key)).toBeUndefined();
    });
  });

  describe('getSearchAnalytics', () => {
    test('should return search analytics', () => {
      const analytics = searchService.getSearchAnalytics();

      expect(analytics).toBeDefined();
      expect(analytics.totalEvents).toBe(3);
      expect(analytics.totalTags).toBeGreaterThan(0);
      expect(analytics.mostUsedTags).toBeDefined();
      expect(analytics.mostReferencedPeople).toBeDefined();
    });
  });
});
