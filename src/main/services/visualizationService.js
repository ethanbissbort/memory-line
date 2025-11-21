/**
 * Visualization Service
 * Generates analytics data for charts and graphs
 */

class VisualizationService {
  constructor(db) {
    this.db = db;
  }

  /**
   * Get timeline density data (events per time period)
   * @param {string} groupBy - Group by: day, week, month, quarter, year
   * @param {string} startDate - Optional start date filter
   * @param {string} endDate - Optional end date filter
   * @returns {Array} Timeline density data
   */
  getTimelineDensity(groupBy = 'month', startDate = null, endDate = null) {
    const formatMap = {
      'day': '%Y-%m-%d',
      'week': '%Y-%W',
      'month': '%Y-%m',
      'quarter': '%Y-Q' + Math.floor((new Date().getMonth() / 3)),
      'year': '%Y'
    };

    const format = formatMap[groupBy] || formatMap.month;
    const conditions = [];
    const params = [];

    if (startDate) {
      conditions.push('start_date >= ?');
      params.push(startDate);
    }
    if (endDate) {
      conditions.push('start_date <= ?');
      params.push(endDate);
    }

    const whereClause = conditions.length > 0 ? `WHERE ${conditions.join(' AND ')}` : '';

    const sql = `
      SELECT
        strftime('${format}', start_date) as period,
        COUNT(*) as event_count,
        COUNT(DISTINCT category) as category_count,
        MIN(start_date) as period_start,
        MAX(start_date) as period_end
      FROM events
      ${whereClause}
      GROUP BY period
      ORDER BY period
    `;

    return this.db.prepare(sql).all(...params);
  }

  /**
   * Get category distribution data
   * @returns {Array} Category distribution with counts and percentages
   */
  getCategoryDistribution() {
    const total = this.db.prepare('SELECT COUNT(*) as count FROM events').get().count;

    const sql = `
      SELECT
        category,
        COUNT(*) as count,
        ROUND(COUNT(*) * 100.0 / ?, 2) as percentage
      FROM events
      GROUP BY category
      ORDER BY count DESC
    `;

    return this.db.prepare(sql).all(total);
  }

  /**
   * Get tag cloud data (most used tags)
   * @param {number} limit - Number of tags to return
   * @returns {Array} Tag usage data
   */
  getTagCloud(limit = 50) {
    const sql = `
      SELECT
        t.name,
        t.color,
        COUNT(et.event_id) as usage_count,
        MIN(e.start_date) as first_used,
        MAX(e.start_date) as last_used
      FROM tags t
      JOIN event_tags et ON t.id = et.tag_id
      JOIN events e ON et.event_id = e.id
      GROUP BY t.id
      ORDER BY usage_count DESC
      LIMIT ?
    `;

    return this.db.prepare(sql).all(limit);
  }

  /**
   * Get people network data (connections between people)
   * @returns {Object} Network graph data
   */
  getPeopleNetwork() {
    // Get all people with their event counts
    const people = this.db.prepare(`
      SELECT
        p.id,
        p.name,
        COUNT(ep.event_id) as event_count
      FROM people p
      JOIN event_people ep ON p.id = ep.person_id
      GROUP BY p.id
      ORDER BY event_count DESC
      LIMIT 100
    `).all();

    // Get connections (people who appear in the same events)
    const connections = this.db.prepare(`
      SELECT
        ep1.person_id as source,
        ep2.person_id as target,
        COUNT(DISTINCT ep1.event_id) as connection_strength
      FROM event_people ep1
      JOIN event_people ep2 ON ep1.event_id = ep2.event_id
      WHERE ep1.person_id < ep2.person_id
      GROUP BY ep1.person_id, ep2.person_id
      HAVING connection_strength > 1
      ORDER BY connection_strength DESC
      LIMIT 200
    `).all();

    return {
      nodes: people.map(p => ({
        id: p.id,
        label: p.name,
        value: p.event_count
      })),
      edges: connections.map(c => ({
        source: c.source,
        target: c.target,
        weight: c.connection_strength
      }))
    };
  }

  /**
   * Get location heatmap data
   * @returns {Array} Location frequency data
   */
  getLocationHeatmap() {
    const sql = `
      SELECT
        l.name,
        l.latitude,
        l.longitude,
        COUNT(el.event_id) as event_count,
        GROUP_CONCAT(DISTINCT e.category) as categories
      FROM locations l
      JOIN event_locations el ON l.id = el.location_id
      JOIN events e ON el.event_id = e.id
      WHERE l.latitude IS NOT NULL AND l.longitude IS NOT NULL
      GROUP BY l.id
      ORDER BY event_count DESC
    `;

    return this.db.prepare(sql).all();
  }

  /**
   * Get era statistics
   * @returns {Array} Era statistics
   */
  getEraStatistics() {
    const sql = `
      SELECT
        er.id,
        er.name,
        er.color,
        er.start_date,
        er.end_date,
        COUNT(e.id) as event_count,
        COUNT(DISTINCT e.category) as category_count,
        COUNT(DISTINCT et.tag_id) as unique_tags,
        COUNT(DISTINCT ep.person_id) as unique_people,
        AVG(
          CASE WHEN e.end_date IS NOT NULL
          THEN (julianday(e.end_date) - julianday(e.start_date)) * 86400
          ELSE 0 END
        ) as avg_event_duration
      FROM eras er
      LEFT JOIN events e ON er.id = e.era_id
      LEFT JOIN event_tags et ON e.id = et.event_id
      LEFT JOIN event_people ep ON e.id = ep.event_id
      GROUP BY er.id
      ORDER BY er.start_date
    `;

    return this.db.prepare(sql).all();
  }

  /**
   * Get activity heatmap (by day of week and hour)
   * @returns {Array} Activity patterns
   */
  getActivityHeatmap() {
    const sql = `
      SELECT
        CAST(strftime('%w', start_date) AS INTEGER) as day_of_week,
        CAST(strftime('%H', start_date) AS INTEGER) as hour_of_day,
        COUNT(*) as event_count
      FROM events
      WHERE start_date IS NOT NULL
      GROUP BY day_of_week, hour_of_day
      ORDER BY day_of_week, hour_of_day
    `;

    const data = this.db.prepare(sql).all();

    // Convert to 2D array for heatmap (7 days x 24 hours)
    const heatmap = Array(7).fill(null).map(() => Array(24).fill(0));

    data.forEach(row => {
      heatmap[row.day_of_week][row.hour_of_day] = row.event_count;
    });

    return {
      raw: data,
      matrix: heatmap,
      labels: {
        days: ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'],
        hours: Array(24).fill(0).map((_, i) => `${i}:00`)
      }
    };
  }

  /**
   * Get trend analysis (events over time with trend line)
   * @param {string} groupBy - Group by: day, week, month, year
   * @returns {Object} Trend data with regression
   */
  getTrendAnalysis(groupBy = 'month') {
    const densityData = this.getTimelineDensity(groupBy);

    if (densityData.length < 2) {
      return { data: densityData, trend: null };
    }

    // Calculate simple linear regression
    const n = densityData.length;
    const x = densityData.map((_, i) => i);
    const y = densityData.map(d => d.event_count);

    const sumX = x.reduce((a, b) => a + b, 0);
    const sumY = y.reduce((a, b) => a + b, 0);
    const sumXY = x.reduce((sum, xi, i) => sum + xi * y[i], 0);
    const sumX2 = x.reduce((sum, xi) => sum + xi * xi, 0);

    const slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
    const intercept = (sumY - slope * sumX) / n;

    const trendLine = x.map(xi => ({
      x: xi,
      y: slope * xi + intercept
    }));

    return {
      data: densityData,
      trend: {
        slope,
        intercept,
        line: trendLine,
        direction: slope > 0.1 ? 'increasing' : slope < -0.1 ? 'decreasing' : 'stable'
      }
    };
  }

  /**
   * Get summary statistics
   * @returns {Object} Overall statistics
   */
  getSummaryStatistics() {
    const stats = {};

    // Total counts
    stats.totalEvents = this.db.prepare('SELECT COUNT(*) as count FROM events').get().count;
    stats.totalEras = this.db.prepare('SELECT COUNT(*) as count FROM eras').get().count;
    stats.totalTags = this.db.prepare('SELECT COUNT(*) as count FROM tags').get().count;
    stats.totalPeople = this.db.prepare('SELECT COUNT(*) as count FROM people').get().count;
    stats.totalLocations = this.db.prepare('SELECT COUNT(*) as count FROM locations').get().count;

    // Date range
    const dateRange = this.db.prepare(`
      SELECT MIN(start_date) as earliest, MAX(start_date) as latest
      FROM events
    `).get();
    stats.earliestEvent = dateRange.earliest;
    stats.latestEvent = dateRange.latest;

    // Calculate timeline span in days
    if (dateRange.earliest && dateRange.latest) {
      const start = new Date(dateRange.earliest);
      const end = new Date(dateRange.latest);
      stats.timelineSpanDays = Math.floor((end - start) / (1000 * 60 * 60 * 24));
    }

    // Events with transcripts
    stats.eventsWithTranscripts = this.db.prepare(`
      SELECT COUNT(*) as count FROM events
      WHERE transcript IS NOT NULL AND transcript != ''
    `).get().count;

    // Events with cross-references
    stats.eventsWithCrossReferences = this.db.prepare(`
      SELECT COUNT(DISTINCT source_event_id) as count
      FROM cross_references
    `).get().count;

    // Average events per day/month/year
    if (stats.timelineSpanDays > 0) {
      stats.avgEventsPerDay = (stats.totalEvents / stats.timelineSpanDays).toFixed(2);
      stats.avgEventsPerMonth = (stats.totalEvents / (stats.timelineSpanDays / 30)).toFixed(2);
      stats.avgEventsPerYear = (stats.totalEvents / (stats.timelineSpanDays / 365)).toFixed(2);
    }

    // Most active category
    const topCategory = this.db.prepare(`
      SELECT category, COUNT(*) as count
      FROM events
      GROUP BY category
      ORDER BY count DESC
      LIMIT 1
    `).get();
    stats.mostActiveCategory = topCategory;

    // Most used tag
    const topTag = this.db.prepare(`
      SELECT t.name, COUNT(*) as count
      FROM tags t
      JOIN event_tags et ON t.id = et.tag_id
      GROUP BY t.id
      ORDER BY count DESC
      LIMIT 1
    `).get();
    stats.mostUsedTag = topTag;

    // Most referenced person
    const topPerson = this.db.prepare(`
      SELECT p.name, COUNT(*) as count
      FROM people p
      JOIN event_people ep ON p.id = ep.person_id
      GROUP BY p.id
      ORDER BY count DESC
      LIMIT 1
    `).get();
    stats.mostReferencedPerson = topPerson;

    // Events by category
    stats.eventsByCategory = this.db.prepare(`
      SELECT category, COUNT(*) as count
      FROM events
      GROUP BY category
      ORDER BY count DESC
    `).all();

    return stats;
  }

  /**
   * Get memory density map (events clustered by time proximity)
   * @param {number} clusterDays - Days to group events together
   * @returns {Array} Memory clusters
   */
  getMemoryDensityMap(clusterDays = 30) {
    const sql = `
      SELECT
        DATE(start_date, '-' || (julianday(start_date) % ${clusterDays}) || ' days') as cluster_date,
        COUNT(*) as event_count,
        GROUP_CONCAT(DISTINCT category) as categories,
        MIN(start_date) as cluster_start,
        MAX(start_date) as cluster_end
      FROM events
      GROUP BY cluster_date
      HAVING event_count > 1
      ORDER BY cluster_date
    `;

    return this.db.prepare(sql).all();
  }

  /**
   * Get relationship strength matrix (connections between tags)
   * @param {number} limit - Number of top tags to analyze
   * @returns {Object} Tag relationship matrix
   */
  getTagRelationshipMatrix(limit = 20) {
    // Get top tags
    const topTags = this.db.prepare(`
      SELECT t.id, t.name
      FROM tags t
      JOIN event_tags et ON t.id = et.tag_id
      GROUP BY t.id
      ORDER BY COUNT(*) DESC
      LIMIT ?
    `).all(limit);

    // Build co-occurrence matrix
    const matrix = {};
    const tagIds = topTags.map(t => t.id);

    for (let i = 0; i < tagIds.length; i++) {
      for (let j = i; j < tagIds.length; j++) {
        const count = this.db.prepare(`
          SELECT COUNT(DISTINCT et1.event_id) as count
          FROM event_tags et1
          JOIN event_tags et2 ON et1.event_id = et2.event_id
          WHERE et1.tag_id = ? AND et2.tag_id = ?
        `).get(tagIds[i], tagIds[j]).count;

        const key = `${tagIds[i]}-${tagIds[j]}`;
        matrix[key] = count;

        if (i !== j) {
          const reverseKey = `${tagIds[j]}-${tagIds[i]}`;
          matrix[reverseKey] = count;
        }
      }
    }

    return {
      tags: topTags,
      matrix
    };
  }

  /**
   * Get comparison data (compare two time periods)
   * @param {string} period1Start - First period start date
   * @param {string} period1End - First period end date
   * @param {string} period2Start - Second period start date
   * @param {string} period2End - Second period end date
   * @returns {Object} Comparison statistics
   */
  compareTimePeriods(period1Start, period1End, period2Start, period2End) {
    const getPeriodStats = (startDate, endDate) => {
      return {
        totalEvents: this.db.prepare(`
          SELECT COUNT(*) as count FROM events
          WHERE start_date BETWEEN ? AND ?
        `).get(startDate, endDate).count,
        categories: this.db.prepare(`
          SELECT category, COUNT(*) as count FROM events
          WHERE start_date BETWEEN ? AND ?
          GROUP BY category
        `).all(startDate, endDate),
        topTags: this.db.prepare(`
          SELECT t.name, COUNT(*) as count
          FROM tags t
          JOIN event_tags et ON t.id = et.tag_id
          JOIN events e ON et.event_id = e.id
          WHERE e.start_date BETWEEN ? AND ?
          GROUP BY t.id
          ORDER BY count DESC
          LIMIT 10
        `).all(startDate, endDate)
      };
    };

    const period1 = getPeriodStats(period1Start, period1End);
    const period2 = getPeriodStats(period2Start, period2End);

    return {
      period1: { ...period1, start: period1Start, end: period1End },
      period2: { ...period2, start: period2Start, end: period2End },
      comparison: {
        eventsDifference: period2.totalEvents - period1.totalEvents,
        eventsPercentChange: period1.totalEvents > 0
          ? ((period2.totalEvents - period1.totalEvents) / period1.totalEvents * 100).toFixed(2)
          : null
      }
    };
  }
}

module.exports = VisualizationService;
