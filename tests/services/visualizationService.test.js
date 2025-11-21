/**
 * Unit tests for VisualizationService
 */

const VisualizationService = require('../../src/main/services/visualizationService');
const Database = require('better-sqlite3');

describe('VisualizationService', () => {
  let db;
  let visualizationService;

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
        relationship_type TEXT
      );
    `);

    // Insert test data
    const era1 = 'era-1';
    db.prepare('INSERT INTO eras (id, name, color, start_date, end_date) VALUES (?, ?, ?, ?, ?)').run(
      era1, 'Test Era', '#ff0000', '2024-01-01', '2024-06-30'
    );

    // Insert events with various dates
    for (let i = 1; i <= 10; i++) {
      const eventId = `event-${i}`;
      const month = i % 3 + 1;
      const category = i % 2 === 0 ? 'Work' : 'Personal';
      const date = `2024-0${month}-${String(i).padStart(2, '0')}`;

      db.prepare(`
        INSERT INTO events (id, title, start_date, category, era_id, transcript)
        VALUES (?, ?, ?, ?, ?, ?)
      `).run(eventId, `Event ${i}`, date, category, era1, `Transcript ${i}`);

      // Add tag
      const tagId = `tag-${i % 3}`;
      const tagName = ['important', 'work', 'personal'][i % 3];
      db.prepare('INSERT OR IGNORE INTO tags (id, name, color) VALUES (?, ?, ?)').run(
        tagId, tagName, '#blue'
      );
      db.prepare('INSERT INTO event_tags (event_id, tag_id) VALUES (?, ?)').run(eventId, tagId);

      // Add person
      if (i <= 3) {
        const personId = `person-${i}`;
        db.prepare('INSERT INTO people (id, name) VALUES (?, ?)').run(personId, `Person ${i}`);
        db.prepare('INSERT INTO event_people (event_id, person_id) VALUES (?, ?)').run(eventId, personId);
      }

      // Add location
      if (i <= 2) {
        const locId = `loc-${i}`;
        db.prepare('INSERT INTO locations (id, name, latitude, longitude) VALUES (?, ?, ?, ?)').run(
          locId, `Location ${i}`, 40.7128 + i, -74.0060 + i
        );
        db.prepare('INSERT INTO event_locations (event_id, location_id) VALUES (?, ?)').run(eventId, locId);
      }
    }

    visualizationService = new VisualizationService(db);
  });

  afterEach(() => {
    db.close();
  });

  describe('getSummaryStatistics', () => {
    test('should return summary statistics', () => {
      const stats = visualizationService.getSummaryStatistics();

      expect(stats).toBeDefined();
      expect(stats.totalEvents).toBe(10);
      expect(stats.totalEras).toBe(1);
      expect(stats.totalTags).toBe(3);
      expect(stats.totalPeople).toBe(3);
      expect(stats.totalLocations).toBe(2);
      expect(stats.earliestEvent).toBeDefined();
      expect(stats.latestEvent).toBeDefined();
    });

    test('should calculate timeline span', () => {
      const stats = visualizationService.getSummaryStatistics();

      expect(stats.timelineSpanDays).toBeGreaterThan(0);
      expect(stats.avgEventsPerDay).toBeDefined();
      expect(stats.avgEventsPerMonth).toBeDefined();
    });

    test('should identify most active category', () => {
      const stats = visualizationService.getSummaryStatistics();

      expect(stats.mostActiveCategory).toBeDefined();
      expect(stats.mostActiveCategory.category).toBeDefined();
      expect(stats.mostActiveCategory.count).toBeGreaterThan(0);
    });

    test('should count events with transcripts', () => {
      const stats = visualizationService.getSummaryStatistics();

      expect(stats.eventsWithTranscripts).toBe(10);
    });
  });

  describe('getCategoryDistribution', () => {
    test('should return category distribution', () => {
      const distribution = visualizationService.getCategoryDistribution();

      expect(distribution).toBeDefined();
      expect(distribution.length).toBe(2);
      expect(distribution[0].category).toBeDefined();
      expect(distribution[0].count).toBeGreaterThan(0);
      expect(distribution[0].percentage).toBeGreaterThan(0);
    });

    test('should sum to 100%', () => {
      const distribution = visualizationService.getCategoryDistribution();
      const totalPercentage = distribution.reduce((sum, cat) => sum + cat.percentage, 0);

      expect(totalPercentage).toBeCloseTo(100, 1);
    });
  });

  describe('getTimelineDensity', () => {
    test('should return timeline density by month', () => {
      const density = visualizationService.getTimelineDensity('month');

      expect(density).toBeDefined();
      expect(density.length).toBeGreaterThan(0);
      expect(density[0].period).toBeDefined();
      expect(density[0].event_count).toBeGreaterThan(0);
    });

    test('should group by different time periods', () => {
      const byDay = visualizationService.getTimelineDensity('day');
      const byMonth = visualizationService.getTimelineDensity('month');
      const byYear = visualizationService.getTimelineDensity('year');

      expect(byDay.length).toBeGreaterThanOrEqual(byMonth.length);
      expect(byYear.length).toBeLessThanOrEqual(byMonth.length);
    });

    test('should filter by date range', () => {
      const all = visualizationService.getTimelineDensity('month');
      const filtered = visualizationService.getTimelineDensity('month', '2024-01-01', '2024-01-31');

      expect(filtered.length).toBeLessThanOrEqual(all.length);
    });
  });

  describe('getTagCloud', () => {
    test('should return tag cloud data', () => {
      const tagCloud = visualizationService.getTagCloud(10);

      expect(tagCloud).toBeDefined();
      expect(tagCloud.length).toBeGreaterThan(0);
      expect(tagCloud[0].name).toBeDefined();
      expect(tagCloud[0].usage_count).toBeGreaterThan(0);
    });

    test('should limit results', () => {
      const tagCloud = visualizationService.getTagCloud(2);

      expect(tagCloud.length).toBeLessThanOrEqual(2);
    });

    test('should sort by usage count', () => {
      const tagCloud = visualizationService.getTagCloud();

      for (let i = 1; i < tagCloud.length; i++) {
        expect(tagCloud[i - 1].usage_count).toBeGreaterThanOrEqual(tagCloud[i].usage_count);
      }
    });
  });

  describe('getPeopleNetwork', () => {
    test('should return people network data', () => {
      const network = visualizationService.getPeopleNetwork();

      expect(network).toBeDefined();
      expect(network.nodes).toBeDefined();
      expect(network.edges).toBeDefined();
      expect(network.nodes.length).toBeGreaterThan(0);
    });

    test('should include node attributes', () => {
      const network = visualizationService.getPeopleNetwork();

      expect(network.nodes[0].id).toBeDefined();
      expect(network.nodes[0].label).toBeDefined();
      expect(network.nodes[0].value).toBeGreaterThan(0);
    });
  });

  describe('getLocationHeatmap', () => {
    test('should return location heatmap data', () => {
      const heatmap = visualizationService.getLocationHeatmap();

      expect(heatmap).toBeDefined();
      expect(heatmap.length).toBe(2);
      expect(heatmap[0].name).toBeDefined();
      expect(heatmap[0].latitude).toBeDefined();
      expect(heatmap[0].longitude).toBeDefined();
      expect(heatmap[0].event_count).toBeGreaterThan(0);
    });
  });

  describe('getEraStatistics', () => {
    test('should return era statistics', () => {
      const eraStats = visualizationService.getEraStatistics();

      expect(eraStats).toBeDefined();
      expect(eraStats.length).toBe(1);
      expect(eraStats[0].name).toBe('Test Era');
      expect(eraStats[0].event_count).toBe(10);
      expect(eraStats[0].category_count).toBeGreaterThan(0);
    });

    test('should include aggregated statistics', () => {
      const eraStats = visualizationService.getEraStatistics();

      expect(eraStats[0].unique_tags).toBeGreaterThan(0);
      expect(eraStats[0].unique_people).toBeGreaterThan(0);
    });
  });

  describe('getActivityHeatmap', () => {
    test('should return activity heatmap', () => {
      const heatmap = visualizationService.getActivityHeatmap();

      expect(heatmap).toBeDefined();
      expect(heatmap.raw).toBeDefined();
      expect(heatmap.matrix).toBeDefined();
      expect(heatmap.labels).toBeDefined();
      expect(heatmap.labels.days.length).toBe(7);
      expect(heatmap.labels.hours.length).toBe(24);
    });

    test('should create 7x24 matrix', () => {
      const heatmap = visualizationService.getActivityHeatmap();

      expect(heatmap.matrix.length).toBe(7);
      expect(heatmap.matrix[0].length).toBe(24);
    });
  });

  describe('getTrendAnalysis', () => {
    test('should return trend analysis', () => {
      const trend = visualizationService.getTrendAnalysis('month');

      expect(trend).toBeDefined();
      expect(trend.data).toBeDefined();
      expect(trend.trend).toBeDefined();
    });

    test('should calculate trend line', () => {
      const trend = visualizationService.getTrendAnalysis('month');

      if (trend.trend) {
        expect(trend.trend.slope).toBeDefined();
        expect(trend.trend.intercept).toBeDefined();
        expect(trend.trend.line).toBeDefined();
        expect(trend.trend.direction).toMatch(/increasing|decreasing|stable/);
      }
    });
  });

  describe('compareTimePeriods', () => {
    test('should compare two time periods', () => {
      const comparison = visualizationService.compareTimePeriods(
        '2024-01-01', '2024-01-31',
        '2024-02-01', '2024-02-29'
      );

      expect(comparison).toBeDefined();
      expect(comparison.period1).toBeDefined();
      expect(comparison.period2).toBeDefined();
      expect(comparison.comparison).toBeDefined();
      expect(comparison.comparison.eventsDifference).toBeDefined();
    });

    test('should calculate percentage change', () => {
      const comparison = visualizationService.compareTimePeriods(
        '2024-01-01', '2024-01-31',
        '2024-02-01', '2024-02-29'
      );

      if (comparison.comparison.eventsPercentChange !== null) {
        expect(typeof comparison.comparison.eventsPercentChange).toBe('string');
      }
    });
  });
});
