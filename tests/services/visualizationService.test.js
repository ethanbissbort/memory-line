/**
 * Unit tests for VisualizationService
 *
 * The test database is built from the canonical production DDL
 * (src/database/schemas/schema.sql) via tests/helpers/createTestDb.js.
 *
 * Fixture layout (10 events across 2020-2021):
 *   ev01 2020-01-05 work        era-1  transcript, tags alpha+beta, Alice+Bob, Home
 *   ev02 2020-01-15 work        era-1  tag alpha, Alice+Bob, Home
 *   ev03 2020-02-10 milestone   era-1  transcript, tag alpha, Alice, Home
 *   ev04 2020-03-01..03-10 travel era-1  tag beta, Paris
 *   ev05 2020-06-15 education   era-1
 *   ev06 2020-11-20 work        era-1
 *   ev07 2021-02-14 relationship era-2 tag gamma
 *   ev08 2021-04-01 work        era-2
 *   ev09 2021-07-04 achievement era-2  transcript, Carol
 *   ev10 2021-07-20 other       era-2
 * Cross-reference: ev01 <-> ev03 (thematic)
 */

const VisualizationService = require('../../src/main/services/visualizationService');
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

describe('VisualizationService', () => {
  let db;
  let visualizationService;

  beforeEach(() => {
    db = createTestDb();

    insertEra(db, {
      era_id: 'era-1', name: 'Early Twenties',
      start_date: '2020-01-01', end_date: '2020-12-31', color_code: '#ff0000'
    });
    insertEra(db, {
      era_id: 'era-2', name: 'New City',
      start_date: '2021-01-01', end_date: null, color_code: '#00ff00'
    });

    const events = [
      { event_id: 'ev01', title: 'Big project kickoff', start_date: '2020-01-05', category: 'work', era_id: 'era-1', raw_transcript: 'Kickoff transcript' },
      { event_id: 'ev02', title: 'Team offsite', start_date: '2020-01-15', category: 'work', era_id: 'era-1' },
      { event_id: 'ev03', title: 'Promotion', start_date: '2020-02-10', category: 'milestone', era_id: 'era-1', raw_transcript: 'Promotion transcript' },
      { event_id: 'ev04', title: 'Paris vacation', start_date: '2020-03-01', end_date: '2020-03-10', category: 'travel', era_id: 'era-1' },
      { event_id: 'ev05', title: 'Night classes', start_date: '2020-06-15', category: 'education', era_id: 'era-1' },
      { event_id: 'ev06', title: 'Crunch time', start_date: '2020-11-20', category: 'work', era_id: 'era-1' },
      { event_id: 'ev07', title: 'Met someone', start_date: '2021-02-14', category: 'relationship', era_id: 'era-2' },
      { event_id: 'ev08', title: 'New job', start_date: '2021-04-01', category: 'work', era_id: 'era-2' },
      { event_id: 'ev09', title: 'Won a race', start_date: '2021-07-04', category: 'achievement', era_id: 'era-2', raw_transcript: 'Race transcript' },
      { event_id: 'ev10', title: 'Moved apartment', start_date: '2021-07-20', category: 'other', era_id: 'era-2' }
    ];
    events.forEach(e => insertEvent(db, e));

    insertTag(db, { tag_id: 'tag-alpha', tag_name: 'alpha' });
    insertTag(db, { tag_id: 'tag-beta', tag_name: 'beta' });
    insertTag(db, { tag_id: 'tag-gamma', tag_name: 'gamma' });
    linkTag(db, 'ev01', 'tag-alpha');
    linkTag(db, 'ev01', 'tag-beta');
    linkTag(db, 'ev02', 'tag-alpha');
    linkTag(db, 'ev03', 'tag-alpha');
    linkTag(db, 'ev04', 'tag-beta');
    linkTag(db, 'ev07', 'tag-gamma');

    insertPerson(db, { person_id: 'p-alice', name: 'Alice' });
    insertPerson(db, { person_id: 'p-bob', name: 'Bob' });
    insertPerson(db, { person_id: 'p-carol', name: 'Carol' });
    linkPerson(db, 'ev01', 'p-alice');
    linkPerson(db, 'ev01', 'p-bob');
    linkPerson(db, 'ev02', 'p-alice');
    linkPerson(db, 'ev02', 'p-bob');
    linkPerson(db, 'ev03', 'p-alice');
    linkPerson(db, 'ev09', 'p-carol');

    insertLocation(db, { location_id: 'loc-home', name: 'Home' });
    insertLocation(db, { location_id: 'loc-paris', name: 'Paris' });
    linkLocation(db, 'ev01', 'loc-home');
    linkLocation(db, 'ev02', 'loc-home');
    linkLocation(db, 'ev03', 'loc-home');
    linkLocation(db, 'ev04', 'loc-paris');

    insertCrossReference(db, {
      reference_id: 'ref-1', event_id_1: 'ev01', event_id_2: 'ev03',
      relationship_type: 'thematic', confidence_score: 0.8
    });

    visualizationService = new VisualizationService(db);
  });

  afterEach(() => {
    db.close();
  });

  describe('getTimelineDensity', () => {
    test('should bucket events per month with period bounds', () => {
      const density = visualizationService.getTimelineDensity('month');

      expect(density.map(d => d.period)).toEqual([
        '2020-01', '2020-02', '2020-03', '2020-06',
        '2020-11', '2021-02', '2021-04', '2021-07'
      ]);

      const jan2020 = density[0];
      expect(jan2020.event_count).toBe(2);
      expect(jan2020.category_count).toBe(1); // both are 'work'
      expect(jan2020.period_start).toBe('2020-01-05');
      expect(jan2020.period_end).toBe('2020-01-15');
    });

    test('should derive quarters from each row date', () => {
      const density = visualizationService.getTimelineDensity('quarter');

      expect(density).toEqual([
        expect.objectContaining({ period: '2020-Q1', event_count: 4 }),
        expect.objectContaining({ period: '2020-Q2', event_count: 1 }),
        expect.objectContaining({ period: '2020-Q4', event_count: 1 }),
        expect.objectContaining({ period: '2021-Q1', event_count: 1 }),
        expect.objectContaining({ period: '2021-Q2', event_count: 1 }),
        expect.objectContaining({ period: '2021-Q3', event_count: 2 })
      ]);
    });

    test('should support day and year granularity', () => {
      const byDay = visualizationService.getTimelineDensity('day');
      expect(byDay.length).toBe(10);
      byDay.forEach(d => expect(d.event_count).toBe(1));

      const byYear = visualizationService.getTimelineDensity('year');
      expect(byYear).toEqual([
        expect.objectContaining({ period: '2020', event_count: 6 }),
        expect.objectContaining({ period: '2021', event_count: 4 })
      ]);
    });

    test('should fall back to month for unknown groupBy', () => {
      const fallback = visualizationService.getTimelineDensity('bogus');
      const byMonth = visualizationService.getTimelineDensity('month');

      expect(fallback).toEqual(byMonth);
    });

    test('should filter by date range', () => {
      const only2020 = visualizationService.getTimelineDensity('month', '2020-01-01', '2020-12-31');

      expect(only2020.length).toBe(5);
      expect(only2020.reduce((sum, d) => sum + d.event_count, 0)).toBe(6);
    });
  });

  describe('getCategoryDistribution', () => {
    test('should return counts and percentages per category', () => {
      const distribution = visualizationService.getCategoryDistribution();

      expect(distribution.length).toBe(7);
      expect(distribution[0]).toEqual({ category: 'work', count: 4, percentage: 40 });
      distribution.slice(1).forEach(row => {
        expect(row.count).toBe(1);
        expect(row.percentage).toBe(10);
      });
    });

    test('should sum to 100%', () => {
      const distribution = visualizationService.getCategoryDistribution();
      const totalPercentage = distribution.reduce((sum, cat) => sum + cat.percentage, 0);

      expect(totalPercentage).toBeCloseTo(100, 1);
    });
  });

  describe('getTagCloud', () => {
    test('should return tag usage with first/last used dates', () => {
      const tagCloud = visualizationService.getTagCloud(10);

      expect(tagCloud[0]).toEqual({
        name: 'alpha',
        usage_count: 3,
        first_used: '2020-01-05',
        last_used: '2020-02-10'
      });
      expect(tagCloud.map(t => t.name)).toEqual(['alpha', 'beta', 'gamma']);
    });

    test('should limit results', () => {
      expect(visualizationService.getTagCloud(2).length).toBe(2);
    });

    test('should sort by usage count descending', () => {
      const tagCloud = visualizationService.getTagCloud();

      for (let i = 1; i < tagCloud.length; i++) {
        expect(tagCloud[i - 1].usage_count).toBeGreaterThanOrEqual(tagCloud[i].usage_count);
      }
    });
  });

  describe('getPeopleNetwork', () => {
    test('should build nodes from people with event counts', () => {
      const network = visualizationService.getPeopleNetwork();

      expect(network.nodes).toEqual([
        { id: 'p-alice', label: 'Alice', value: 3 },
        { id: 'p-bob', label: 'Bob', value: 2 },
        { id: 'p-carol', label: 'Carol', value: 1 }
      ]);
    });

    test('should only create edges for pairs sharing more than one event', () => {
      const network = visualizationService.getPeopleNetwork();

      // Alice and Bob co-occur in ev01+ev02; Carol shares nothing.
      expect(network.edges).toEqual([
        { source: 'p-alice', target: 'p-bob', weight: 2 }
      ]);
    });
  });

  describe('getLocationHeatmap', () => {
    test('should return frequency data with null coordinates', () => {
      const heatmap = visualizationService.getLocationHeatmap();

      expect(heatmap.length).toBe(2);
      // The locations table has no lat/lng columns; the service explicitly
      // returns null coordinates so map callers degrade gracefully.
      heatmap.forEach(row => {
        expect(row.latitude).toBeNull();
        expect(row.longitude).toBeNull();
      });

      expect(heatmap[0].name).toBe('Home');
      expect(heatmap[0].event_count).toBe(3);
      expect(heatmap[0].categories.split(',').sort()).toEqual(['milestone', 'work']);
      expect(heatmap[1]).toEqual(expect.objectContaining({ name: 'Paris', event_count: 1 }));
    });
  });

  describe('getEraStatistics', () => {
    test('should return one row per era ordered by era start date', () => {
      const eraStats = visualizationService.getEraStatistics();

      expect(eraStats.length).toBe(2);
      expect(eraStats.map(e => e.name)).toEqual(['Early Twenties', 'New City']);
      expect(eraStats[0].id).toBe('era-1');
      expect(eraStats[0].color).toBe('#ff0000');
    });

    test('should aggregate distinct tags, people and categories per era', () => {
      const [era1, era2] = visualizationService.getEraStatistics();

      expect(era1.unique_tags).toBe(2);   // alpha, beta
      expect(era1.unique_people).toBe(2); // Alice, Bob
      expect(era1.category_count).toBe(4);
      expect(era2.unique_tags).toBe(1);   // gamma
      expect(era2.unique_people).toBe(1); // Carol
      expect(era2.category_count).toBe(4);
    });

    // KNOWN PRODUCT BUG (src/main/services/visualizationService.js:195):
    // era-1 really has 6 events, but the LEFT JOINs onto event_tags and
    // event_people multiply rows before COUNT(e.event_id), inflating the
    // count to 10 (ev01: 2 tags x 2 people = 4 rows, ev02: 1x2 = 2, ...).
    // avg_event_duration is skewed by the same fan-out. The fix is
    // COUNT(DISTINCT e.event_id). This test asserts the CORRECT behavior and
    // is marked failing so the suite stays green while the bug is tracked.
    test('event_count should count distinct events per era (join fan-out)', () => {
      const [era1, era2] = visualizationService.getEraStatistics();

      expect(era1.event_count).toBe(6); // currently inflated to 10
      expect(era2.event_count).toBe(4); // no fan-out here, happens to be right
    });
  });

  describe('getActivityHeatmap', () => {
    test('should return raw rows, 7x24 matrix and labels', () => {
      const heatmap = visualizationService.getActivityHeatmap();

      expect(heatmap.matrix.length).toBe(7);
      heatmap.matrix.forEach(row => expect(row.length).toBe(24));
      expect(heatmap.labels.days.length).toBe(7);
      expect(heatmap.labels.hours.length).toBe(24);
      expect(Array.isArray(heatmap.raw)).toBe(true);
    });

    test('should place all events at hour 0 because start_date has no time part', () => {
      const heatmap = visualizationService.getActivityHeatmap();

      const total = heatmap.matrix.flat().reduce((a, b) => a + b, 0);
      const hourZeroTotal = heatmap.matrix.reduce((sum, day) => sum + day[0], 0);

      expect(total).toBe(10);
      expect(hourZeroTotal).toBe(10);
      heatmap.raw.forEach(row => expect(row.hour_of_day).toBe(0));
    });
  });

  describe('getTrendAnalysis', () => {
    test('should return density data plus a regression trend', () => {
      const trend = visualizationService.getTrendAnalysis('month');

      expect(trend.data.length).toBe(8);
      expect(trend.trend).not.toBeNull();
      expect(Number.isFinite(trend.trend.slope)).toBe(true);
      expect(Number.isFinite(trend.trend.intercept)).toBe(true);
      expect(trend.trend.line.length).toBe(8);
      expect(['increasing', 'decreasing', 'stable']).toContain(trend.trend.direction);
    });

    test('should return null trend with fewer than two periods', () => {
      const trend = visualizationService.getTrendAnalysis('year');
      expect(trend.trend).not.toBeNull(); // 2 years -> regression possible

      const single = visualizationService.getTimelineDensity('year', '2020-01-01', '2020-12-31');
      expect(single.length).toBe(1); // sanity: a single-period window exists

      // Direct check of the <2 branch via an empty database
      const emptyDb = createTestDb();
      const emptyService = new VisualizationService(emptyDb);
      expect(emptyService.getTrendAnalysis('month')).toEqual({ data: [], trend: null });
      emptyDb.close();
    });
  });

  describe('getSummaryStatistics', () => {
    test('should return totals for all entities', () => {
      const stats = visualizationService.getSummaryStatistics();

      expect(stats.totalEvents).toBe(10);
      expect(stats.totalEras).toBe(2);
      expect(stats.totalTags).toBe(3);
      expect(stats.totalPeople).toBe(3);
      expect(stats.totalLocations).toBe(2);
    });

    test('should compute date range and averages', () => {
      const stats = visualizationService.getSummaryStatistics();

      expect(stats.earliestEvent).toBe('2020-01-05');
      expect(stats.latestEvent).toBe('2021-07-20');
      expect(stats.timelineSpanDays).toBe(562);
      expect(stats.avgEventsPerDay).toBe((10 / 562).toFixed(2));
      expect(typeof stats.avgEventsPerMonth).toBe('string');
      expect(typeof stats.avgEventsPerYear).toBe('string');
    });

    test('should count transcripts and cross-referenced events', () => {
      const stats = visualizationService.getSummaryStatistics();

      expect(stats.eventsWithTranscripts).toBe(3);   // ev01, ev03, ev09
      expect(stats.eventsWithCrossReferences).toBe(2); // ev01 + ev03 via ref-1
    });

    test('should surface top category, tag and person', () => {
      const stats = visualizationService.getSummaryStatistics();

      expect(stats.mostActiveCategory).toEqual({ category: 'work', count: 4 });
      expect(stats.mostUsedTag).toEqual({ name: 'alpha', count: 3 });
      expect(stats.mostReferencedPerson).toEqual({ name: 'Alice', count: 3 });
      expect(stats.eventsByCategory.length).toBe(7);
    });
  });

  describe('getMemoryDensityMap', () => {
    test('should return only clusters with more than one event', () => {
      const clusters = visualizationService.getMemoryDensityMap(30);

      clusters.forEach(cluster => {
        expect(cluster.event_count).toBeGreaterThan(1);
        expect(cluster.cluster_start <= cluster.cluster_end).toBe(true);
        expect(cluster.categories).toBeDefined();
      });
      const clustered = clusters.reduce((sum, c) => sum + c.event_count, 0);
      expect(clustered).toBeLessThanOrEqual(10);
    });
  });

  describe('getTagRelationshipMatrix', () => {
    test('should build a symmetric co-occurrence matrix over top tags', () => {
      const { tags, matrix } = visualizationService.getTagRelationshipMatrix(2);

      expect(tags).toEqual([
        { id: 'tag-alpha', name: 'alpha' },
        { id: 'tag-beta', name: 'beta' }
      ]);

      expect(matrix['tag-alpha-tag-alpha']).toBe(3); // events tagged alpha
      expect(matrix['tag-beta-tag-beta']).toBe(2);   // events tagged beta
      expect(matrix['tag-alpha-tag-beta']).toBe(1);  // only ev01 has both
      expect(matrix['tag-beta-tag-alpha']).toBe(1);  // symmetric
    });

    test('should respect the tag limit', () => {
      const { tags } = visualizationService.getTagRelationshipMatrix(1);
      expect(tags).toEqual([{ id: 'tag-alpha', name: 'alpha' }]);
    });
  });

  describe('compareTimePeriods', () => {
    test('should compare event volumes between two periods', () => {
      const comparison = visualizationService.compareTimePeriods(
        '2020-01-01', '2020-12-31',
        '2021-01-01', '2021-12-31'
      );

      expect(comparison.period1.totalEvents).toBe(6);
      expect(comparison.period2.totalEvents).toBe(4);
      expect(comparison.period1.start).toBe('2020-01-01');
      expect(comparison.period2.end).toBe('2021-12-31');
      expect(comparison.comparison.eventsDifference).toBe(-2);
      expect(comparison.comparison.eventsPercentChange).toBe('-33.33');

      expect(comparison.period1.topTags[0]).toEqual(
        expect.objectContaining({ name: 'alpha', count: 3 })
      );
      expect(Array.isArray(comparison.period1.categories)).toBe(true);
    });

    test('should return null percent change when the baseline period is empty', () => {
      const comparison = visualizationService.compareTimePeriods(
        '1990-01-01', '1990-12-31',
        '2020-01-01', '2020-12-31'
      );

      expect(comparison.period1.totalEvents).toBe(0);
      expect(comparison.comparison.eventsDifference).toBe(6);
      expect(comparison.comparison.eventsPercentChange).toBeNull();
    });
  });

  describe('empty database safety', () => {
    let emptyDb;
    let emptyService;

    beforeEach(() => {
      emptyDb = createTestDb();
      emptyService = new VisualizationService(emptyDb);
    });

    afterEach(() => {
      emptyDb.close();
    });

    test('list-shaped analytics should return empty arrays', () => {
      expect(emptyService.getTimelineDensity('month')).toEqual([]);
      expect(emptyService.getTimelineDensity('quarter')).toEqual([]);
      expect(emptyService.getCategoryDistribution()).toEqual([]);
      expect(emptyService.getTagCloud()).toEqual([]);
      expect(emptyService.getLocationHeatmap()).toEqual([]);
      expect(emptyService.getEraStatistics()).toEqual([]);
      expect(emptyService.getMemoryDensityMap()).toEqual([]);
    });

    test('object-shaped analytics should be well-formed', () => {
      expect(emptyService.getPeopleNetwork()).toEqual({ nodes: [], edges: [] });

      const heatmap = emptyService.getActivityHeatmap();
      expect(heatmap.raw).toEqual([]);
      expect(heatmap.matrix.flat().reduce((a, b) => a + b, 0)).toBe(0);

      expect(emptyService.getTrendAnalysis('month')).toEqual({ data: [], trend: null });
      expect(emptyService.getTagRelationshipMatrix()).toEqual({ tags: [], matrix: {} });
    });

    test('getSummaryStatistics should not throw with no data', () => {
      const stats = emptyService.getSummaryStatistics();

      expect(stats.totalEvents).toBe(0);
      expect(stats.earliestEvent).toBeNull();
      expect(stats.latestEvent).toBeNull();
      expect(stats.timelineSpanDays).toBeUndefined();
      expect(stats.avgEventsPerDay).toBeUndefined();
      expect(stats.mostActiveCategory).toBeUndefined();
      expect(stats.eventsWithTranscripts).toBe(0);
      expect(stats.eventsWithCrossReferences).toBe(0);
      expect(stats.eventsByCategory).toEqual([]);
    });

    test('compareTimePeriods should handle two empty periods', () => {
      const comparison = emptyService.compareTimePeriods(
        '2020-01-01', '2020-12-31', '2021-01-01', '2021-12-31'
      );

      expect(comparison.period1.totalEvents).toBe(0);
      expect(comparison.period2.totalEvents).toBe(0);
      expect(comparison.comparison.eventsDifference).toBe(0);
      expect(comparison.comparison.eventsPercentChange).toBeNull();
    });
  });
});
