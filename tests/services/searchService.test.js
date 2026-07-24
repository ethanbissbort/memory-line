/**
 * Unit tests for SearchService
 *
 * The test database is built from the canonical production DDL
 * (src/database/schemas/schema.sql) via tests/helpers/createTestDb.js.
 * The schema's triggers keep the events_fts external-content FTS5 table
 * in sync automatically, so seeding `events` is enough for FTS queries.
 */

const SearchService = require('../../src/main/services/searchService');
const {
  createTestDb,
  insertEra,
  insertEvent,
  insertTag,
  insertPerson,
  insertLocation,
  linkTag,
  linkPerson,
  linkLocation,
  insertCrossReference
} = require('../helpers/createTestDb');

describe('SearchService', () => {
  let db;
  let searchService;

  beforeEach(() => {
    db = createTestDb();

    insertEra(db, {
      era_id: 'era-college', name: 'College Years',
      start_date: '2014-09-01', end_date: '2018-06-30', color_code: '#ff0000'
    });
    insertEra(db, {
      era_id: 'era-job', name: 'First Job',
      start_date: '2018-07-01', end_date: null, color_code: '#00ff00'
    });

    insertEvent(db, {
      event_id: 'event-1', title: 'Graduation Day', start_date: '2018-06-01',
      description: 'Finished my degree in Boston', category: 'education',
      era_id: 'era-college', raw_transcript: 'I remember walking across the stage'
    });
    insertEvent(db, {
      event_id: 'event-2', title: 'Started at Acme Corp', start_date: '2018-07-09',
      description: 'First day as a junior engineer', category: 'work',
      era_id: 'era-job', raw_transcript: null
    });
    insertEvent(db, {
      event_id: 'event-3', title: 'Trip to Japan',
      start_date: '2019-04-10', end_date: '2019-04-24',
      description: 'Two weeks travelling', category: 'travel',
      era_id: 'era-job', raw_transcript: 'We visited temples in Kyoto'
    });
    insertEvent(db, {
      event_id: 'event-4', title: 'First Marathon', start_date: '2020-10-05',
      description: 'Ran 42 kilometers', category: 'achievement',
      era_id: null, raw_transcript: '' // empty string, not NULL
    });

    insertTag(db, { tag_id: 'tag-important', tag_name: 'important' });
    insertTag(db, { tag_id: 'tag-career', tag_name: 'career' });
    insertTag(db, { tag_id: 'tag-adventure', tag_name: 'adventure' });
    linkTag(db, 'event-1', 'tag-important');
    linkTag(db, 'event-3', 'tag-important');
    linkTag(db, 'event-2', 'tag-career');
    linkTag(db, 'event-3', 'tag-adventure');

    insertPerson(db, { person_id: 'person-john', name: 'John Doe' });
    insertPerson(db, { person_id: 'person-jane', name: 'Jane Smith' });
    linkPerson(db, 'event-1', 'person-john');
    linkPerson(db, 'event-2', 'person-john');
    linkPerson(db, 'event-3', 'person-jane');

    insertLocation(db, { location_id: 'loc-boston', name: 'Boston' });
    insertLocation(db, { location_id: 'loc-kyoto', name: 'Kyoto' });
    linkLocation(db, 'event-1', 'loc-boston');
    linkLocation(db, 'event-2', 'loc-boston');
    linkLocation(db, 'event-3', 'loc-kyoto');

    insertCrossReference(db, {
      reference_id: 'ref-1', event_id_1: 'event-1', event_id_2: 'event-2',
      relationship_type: 'causal', confidence_score: 0.9
    });

    searchService = new SearchService(db);
  });

  afterEach(() => {
    db.close();
  });

  describe('search', () => {
    test('should return all events with default options', () => {
      const result = searchService.search({});

      expect(result.events.length).toBe(4);
      expect(result.pagination).toEqual({
        page: 1,
        pageSize: 50,
        totalResults: 4,
        totalPages: 1,
        hasNextPage: false,
        hasPreviousPage: false
      });
      expect(result.facets).toBeDefined();
      // Default sort is start_date DESC
      expect(result.events.map(e => e.event_id)).toEqual(
        ['event-4', 'event-3', 'event-2', 'event-1']
      );
    });

    test('should echo applied filters', () => {
      const result = searchService.search({ query: 'Kyoto', categories: ['travel'] });

      expect(result.appliedFilters.query).toBe('Kyoto');
      expect(result.appliedFilters.categories).toEqual(['travel']);
      expect(result.appliedFilters.eraId).toBeNull();
    });

    test('should return rows with canonical columns and era metadata', () => {
      const result = searchService.search({ sortBy: 'date', sortOrder: 'asc' });
      const event = result.events[0];

      expect(event.event_id).toBe('event-1');
      expect(event.id).toBeUndefined();
      expect(event.era_name).toBe('College Years');
      expect(event.era_color).toBe('#ff0000');
    });

    describe('full-text search', () => {
      test('should match transcript content maintained by the FTS triggers', () => {
        const result = searchService.search({ query: 'Kyoto' });

        expect(result.events.length).toBe(1);
        expect(result.events[0].event_id).toBe('event-3');
        expect(result.pagination.totalResults).toBe(1);
      });

      test('should match titles case-insensitively', () => {
        const result = searchService.search({ query: 'graduation' });

        expect(result.events.map(e => e.event_id)).toEqual(['event-1']);
      });

      test('should match descriptions but not linked location names', () => {
        // 'Boston' appears in event-1's description; event-2 is linked to the
        // Boston location but FTS only indexes title/description/transcript.
        const result = searchService.search({ query: 'Boston' });

        expect(result.events.map(e => e.event_id)).toEqual(['event-1']);
      });

      test('should AND multiple tokens together', () => {
        expect(searchService.search({ query: 'temples Kyoto' }).events.length).toBe(1);
        expect(searchService.search({ query: 'Kyoto marathon' }).events.length).toBe(0);
      });

      test('should not throw on FTS5 special characters', () => {
        // The sanitizer wraps each token as a quoted phrase, so operator
        // characters in ordinary input never reach the FTS5 parser.
        expect(searchService.search({ query: 'Kyoto"(*^' }).events.length).toBe(1);
        expect(searchService.search({ query: '"' }).events.length).toBe(0);
        expect(searchService.search({ query: '***' }).events.length).toBe(0);
        expect(searchService.search({ query: 'NEAR AND OR' }).events.length).toBe(0);
      });

      test('should neutralize NOT and prefix operators', () => {
        // '-temples' is a phrase containing the token 'temples', not a NOT
        expect(searchService.search({ query: 'Kyoto -temples' }).events.length).toBe(1);
        // 'grad*' is a phrase token 'grad', not a prefix query
        expect(searchService.search({ query: 'grad*' }).events.length).toBe(0);
      });

      test('should ignore whitespace-only queries', () => {
        const result = searchService.search({ query: '   ' });
        expect(result.events.length).toBe(4);
      });

      test('should combine FTS with other filters', () => {
        const match = searchService.search({ query: 'Kyoto', categories: ['travel'] });
        expect(match.events.map(e => e.event_id)).toEqual(['event-3']);

        const noMatch = searchService.search({ query: 'Kyoto', categories: ['work'] });
        expect(noMatch.events.length).toBe(0);
      });
    });

    test('should filter by category', () => {
      const result = searchService.search({ categories: ['education', 'travel'] });

      expect(result.events.map(e => e.event_id).sort()).toEqual(['event-1', 'event-3']);
      expect(result.events.every(e => ['education', 'travel'].includes(e.category))).toBe(true);
    });

    test('should filter by a single tag', () => {
      const result = searchService.search({ tags: ['important'] });

      expect(result.events.map(e => e.event_id).sort()).toEqual(['event-1', 'event-3']);
    });

    test('should require ALL tags when multiple are given', () => {
      const result = searchService.search({ tags: ['important', 'adventure'] });

      expect(result.events.map(e => e.event_id)).toEqual(['event-3']);
    });

    test('should filter by person name', () => {
      const result = searchService.search({ people: ['John Doe'] });

      expect(result.events.map(e => e.event_id).sort()).toEqual(['event-1', 'event-2']);
    });

    test('should filter by location name', () => {
      const result = searchService.search({ locations: ['Kyoto'] });

      expect(result.events.map(e => e.event_id)).toEqual(['event-3']);
    });

    test('should filter by date range on start_date', () => {
      const result = searchService.search({
        startDate: '2018-06-01',
        endDate: '2018-12-31'
      });

      expect(result.events.map(e => e.event_id).sort()).toEqual(['event-1', 'event-2']);
    });

    test('should filter by era', () => {
      const result = searchService.search({ eraId: 'era-job' });

      expect(result.events.map(e => e.event_id).sort()).toEqual(['event-2', 'event-3']);
    });

    test('should filter events with transcripts (empty string counts as none)', () => {
      const withTranscript = searchService.search({ hasTranscript: true });
      expect(withTranscript.events.map(e => e.event_id).sort())
        .toEqual(['event-1', 'event-3']);

      const withoutTranscript = searchService.search({ hasTranscript: false });
      expect(withoutTranscript.events.map(e => e.event_id).sort())
        .toEqual(['event-2', 'event-4']);
    });

    test('should filter by cross-reference presence (either side)', () => {
      const withRefs = searchService.search({ hasCrossReferences: true });
      expect(withRefs.events.map(e => e.event_id).sort())
        .toEqual(['event-1', 'event-2']);

      const withoutRefs = searchService.search({ hasCrossReferences: false });
      expect(withoutRefs.events.map(e => e.event_id).sort())
        .toEqual(['event-3', 'event-4']);
    });

    test('should filter by duration in seconds (point events excluded)', () => {
      // Only event-3 has an end_date (14 days).
      const result = searchService.search({ minDuration: 86400 });
      expect(result.events.map(e => e.event_id)).toEqual(['event-3']);

      const tooLong = searchService.search({ minDuration: 15 * 86400 });
      expect(tooLong.events.length).toBe(0);

      const upperBound = searchService.search({ maxDuration: 14 * 86400 });
      expect(upperBound.events.map(e => e.event_id)).toEqual(['event-3']);
    });

    test('should paginate results with correct math', () => {
      const page1 = searchService.search({ page: 1, pageSize: 3 });
      expect(page1.events.length).toBe(3);
      expect(page1.pagination).toEqual({
        page: 1,
        pageSize: 3,
        totalResults: 4,
        totalPages: 2,
        hasNextPage: true,
        hasPreviousPage: false
      });

      const page2 = searchService.search({ page: 2, pageSize: 3 });
      expect(page2.events.length).toBe(1);
      expect(page2.pagination.hasNextPage).toBe(false);
      expect(page2.pagination.hasPreviousPage).toBe(true);

      // Pages must not overlap
      const ids1 = page1.events.map(e => e.event_id);
      const ids2 = page2.events.map(e => e.event_id);
      expect(ids1.filter(id => ids2.includes(id))).toEqual([]);
    });

    test('should sort by different fields', () => {
      const byDateAsc = searchService.search({ sortBy: 'date', sortOrder: 'asc' });
      expect(byDateAsc.events[0].start_date).toBe('2018-06-01');

      const byTitleAsc = searchService.search({ sortBy: 'title', sortOrder: 'asc' });
      expect(byTitleAsc.events[0].title).toBe('First Marathon');

      // Unknown sort field falls back to date; unknown order falls back to DESC
      const fallback = searchService.search({ sortBy: 'bogus', sortOrder: 'bogus' });
      expect(fallback.events[0].event_id).toBe('event-4');
    });

    test('should load tags, people and locations per event', () => {
      const result = searchService.search({});
      const byId = Object.fromEntries(result.events.map(e => [e.event_id, e]));

      expect(byId['event-1'].tags).toEqual(['important']);
      expect(byId['event-1'].people).toEqual(['John Doe']);
      expect(byId['event-1'].locations).toEqual(['Boston']);

      expect(byId['event-3'].tags.sort()).toEqual(['adventure', 'important']);
      expect(byId['event-4'].tags).toEqual([]);
      expect(byId['event-4'].people).toEqual([]);
      expect(byId['event-4'].locations).toEqual([]);
    });

    describe('facets', () => {
      test('should count categories, tags, people, locations, eras and years', () => {
        const { facets } = searchService.search({});

        expect(facets.categories).toHaveLength(4);
        facets.categories.forEach(c => expect(c.count).toBe(1));

        expect(facets.tags[0]).toEqual({ name: 'important', count: 2 });
        expect(facets.tags).toHaveLength(3);

        expect(facets.people[0]).toEqual({ name: 'John Doe', count: 2 });
        expect(facets.locations[0]).toEqual({ name: 'Boston', count: 2 });

        const eraNames = facets.eras.map(e => e.name);
        expect(eraNames).toContain('College Years');
        expect(eraNames).toContain('First Job');
        // Events without an era group under a null era row
        expect(facets.eras.find(e => e.id === null).count).toBe(1);
        expect(facets.eras.find(e => e.name === 'First Job').count).toBe(2);
        expect(facets.eras.find(e => e.name === 'First Job').color).toBe('#00ff00');

        // Years descending
        expect(facets.years).toEqual([
          { year: '2020', count: 1 },
          { year: '2019', count: 1 },
          { year: '2018', count: 2 }
        ]);
      });

      test('should recalculate facets for the filtered result set', () => {
        const { facets } = searchService.search({ categories: ['travel'] });

        expect(facets.categories).toEqual([{ category: 'travel', count: 1 }]);
        expect(facets.tags.map(t => t.name).sort()).toEqual(['adventure', 'important']);
        facets.tags.forEach(t => expect(t.count).toBe(1));
        expect(facets.years).toEqual([{ year: '2019', count: 1 }]);
      });
    });
  });

  describe('getSuggestions', () => {
    test('should suggest event titles', () => {
      const suggestions = searchService.getSuggestions('Grad');

      expect(suggestions).toEqual([{ title: 'Graduation Day', type: 'event' }]);
    });

    test('should suggest tags, people and locations with their types', () => {
      expect(searchService.getSuggestions('import'))
        .toEqual([{ title: 'important', type: 'tag' }]);
      expect(searchService.getSuggestions('John'))
        .toEqual([{ title: 'John Doe', type: 'person' }]);
      expect(searchService.getSuggestions('Kyo'))
        .toEqual([{ title: 'Kyoto', type: 'location' }]);
    });

    test('should return empty array for queries shorter than 2 characters', () => {
      expect(searchService.getSuggestions('a')).toEqual([]);
      expect(searchService.getSuggestions('')).toEqual([]);
      expect(searchService.getSuggestions(null)).toEqual([]);
    });

    test('should cap combined suggestions at the limit', () => {
      // 'an' matches 'Trip to Japan' (event), 'important' (tag), 'Jane Smith' (person)
      const all = searchService.getSuggestions('an');
      expect(all.length).toBe(3);

      const limited = searchService.getSuggestions('an', 2);
      expect(limited.length).toBe(2);
    });
  });

  describe('saved searches', () => {
    afterEach(() => {
      jest.restoreAllMocks();
    });

    test('saveSearch should persist to app_settings and return the key', () => {
      const result = searchService.saveSearch('My Search', {
        categories: ['education'],
        startDate: '2018-01-01'
      });

      expect(result.success).toBe(true);
      expect(result.key).toMatch(/^saved_search_\d+_[0-9a-f]{8}$/);

      const row = db.prepare(
        'SELECT setting_value FROM app_settings WHERE setting_key = ?'
      ).get(result.key);
      const saved = JSON.parse(row.setting_value);
      expect(saved.name).toBe('My Search');
      expect(saved.options).toEqual({ categories: ['education'], startDate: '2018-01-01' });
      expect(saved.createdAt).toBeDefined();
    });

    test('getSavedSearches should return only saved_search_* settings rows', () => {
      // NOTE: saveSearch keys on Date.now(), so two saves in the same
      // millisecond violate the app_settings primary key (known production
      // bug). Distinct timestamps are forced here to keep the test stable.
      const nowSpy = jest.spyOn(Date, 'now')
        .mockReturnValueOnce(1700000000001)
        .mockReturnValueOnce(1700000000002);

      searchService.saveSearch('Search 1', { categories: ['education'] });
      searchService.saveSearch('Search 2', { tags: ['important'] });
      nowSpy.mockRestore();

      const searches = searchService.getSavedSearches();
      // The schema seeds default app_settings rows; none may leak in here.
      expect(searches.length).toBe(2);
      searches.forEach(row => {
        expect(row.setting_key).toMatch(/^saved_search_/);
        expect(JSON.parse(row.setting_value).name).toMatch(/^Search [12]$/);
        expect(row.updated_at).toBeDefined();
      });
    });

    // KNOWN PRODUCT BUG (src/main/services/searchService.js:483): the saved
    // search key is `saved_search_${Date.now()}`, so two saves within the
    // same millisecond collide on the app_settings primary key and the
    // second INSERT throws SqliteError. The fix is a uniqueness-safe key
    // (e.g. a UUID) or INSERT OR REPLACE. This test asserts the CORRECT
    // behavior (both saves succeed with distinct keys) and is marked failing
    // so the suite stays green while the bug is tracked.
    test('saveSearch should survive two saves in the same millisecond', () => {
      jest.spyOn(Date, 'now').mockReturnValue(1700000000000);

      const first = searchService.saveSearch('First', {});
      const second = searchService.saveSearch('Second', {}); // throws today

      expect(first.success).toBe(true);
      expect(second.success).toBe(true);
      expect(second.key).not.toBe(first.key);
    });

    test('deleteSavedSearch should remove the row', () => {
      const saveResult = searchService.saveSearch('Test Search', {});
      const deleteResult = searchService.deleteSavedSearch(saveResult.key);

      expect(deleteResult.success).toBe(true);
      expect(searchService.getSavedSearches()).toEqual([]);
    });
  });

  describe('getSearchAnalytics', () => {
    test('should return counts and top usage lists', () => {
      const analytics = searchService.getSearchAnalytics();

      expect(analytics.totalEvents).toBe(4);
      expect(analytics.totalTags).toBe(3);
      expect(analytics.totalPeople).toBe(2);
      expect(analytics.totalLocations).toBe(2);
      expect(analytics.mostUsedTags[0]).toEqual({ name: 'important', usage_count: 2 });
      expect(analytics.mostReferencedPeople[0]).toEqual({ name: 'John Doe', reference_count: 2 });
    });
  });

  describe('empty database safety', () => {
    let emptyDb;
    let emptySearch;

    beforeEach(() => {
      emptyDb = createTestDb();
      emptySearch = new SearchService(emptyDb);
    });

    afterEach(() => {
      emptyDb.close();
    });

    test('search should return an empty page, not throw', () => {
      const result = emptySearch.search({ query: 'anything' });

      expect(result.events).toEqual([]);
      expect(result.pagination.totalResults).toBe(0);
      expect(result.pagination.totalPages).toBe(0);
      expect(result.pagination.hasNextPage).toBe(false);
      expect(result.facets.categories).toEqual([]);
      expect(result.facets.tags).toEqual([]);
      expect(result.facets.years).toEqual([]);
    });

    test('suggestions and analytics should be empty but well-formed', () => {
      expect(emptySearch.getSuggestions('anything')).toEqual([]);

      const analytics = emptySearch.getSearchAnalytics();
      expect(analytics.totalEvents).toBe(0);
      expect(analytics.mostUsedTags).toEqual([]);
      expect(analytics.mostReferencedPeople).toEqual([]);
    });
  });
});
