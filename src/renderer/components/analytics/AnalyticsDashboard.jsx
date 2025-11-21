import React, { useState, useEffect } from 'react';
import './AnalyticsDashboard.css';

const AnalyticsDashboard = ({ onClose }) => {
  const [stats, setStats] = useState(null);
  const [categoryData, setCategoryData] = useState([]);
  const [timelineDensity, setTimelineDensity] = useState([]);
  const [tagCloud, setTagCloud] = useState([]);
  const [eraStats, setEraStats] = useState([]);
  const [trendAnalysis, setTrendAnalysis] = useState(null);
  const [loading, setLoading] = useState(true);
  const [activeTab, setActiveTab] = useState('overview');
  const [timeGrouping, setTimeGrouping] = useState('month');

  useEffect(() => {
    loadDashboardData();
  }, []);

  useEffect(() => {
    if (activeTab === 'timeline') {
      loadTimelineDensity();
    }
  }, [timeGrouping, activeTab]);

  const loadDashboardData = async () => {
    setLoading(true);
    try {
      const [summaryStats, categories, tags, eras, trend] = await Promise.all([
        window.electronAPI.analytics.getSummaryStatistics(),
        window.electronAPI.analytics.getCategoryDistribution(),
        window.electronAPI.analytics.getTagCloud(30),
        window.electronAPI.analytics.getEraStatistics(),
        window.electronAPI.analytics.getTrendAnalysis('month')
      ]);

      setStats(summaryStats);
      setCategoryData(categories);
      setTagCloud(tags);
      setEraStats(eras);
      setTrendAnalysis(trend);
    } catch (error) {
      console.error('Error loading dashboard data:', error);
    } finally {
      setLoading(false);
    }
  };

  const loadTimelineDensity = async () => {
    try {
      const density = await window.electronAPI.analytics.getTimelineDensity(timeGrouping);
      setTimelineDensity(density);
    } catch (error) {
      console.error('Error loading timeline density:', error);
    }
  };

  const renderBarChart = (data, valueKey, labelKey, colorKey) => {
    if (!data || data.length === 0) return null;

    const maxValue = Math.max(...data.map(d => d[valueKey]));
    const colors = ['#3b82f6', '#8b5cf6', '#ec4899', '#f59e0b', '#10b981', '#06b6d4', '#6366f1'];

    return (
      <div className="bar-chart">
        {data.map((item, index) => (
          <div key={index} className="bar-item">
            <div className="bar-label">{item[labelKey]}</div>
            <div className="bar-container">
              <div
                className="bar-fill"
                style={{
                  width: `${(item[valueKey] / maxValue) * 100}%`,
                  backgroundColor: item[colorKey] || colors[index % colors.length]
                }}
              >
                <span className="bar-value">{item[valueKey]}</span>
              </div>
            </div>
          </div>
        ))}
      </div>
    );
  };

  const renderPieChart = (data) => {
    if (!data || data.length === 0) return null;

    const total = data.reduce((sum, item) => sum + item.count, 0);
    let currentAngle = 0;
    const radius = 100;
    const center = 120;

    return (
      <div className="pie-chart-container">
        <svg width={center * 2} height={center * 2} className="pie-chart">
          {data.map((item, index) => {
            const percentage = (item.count / total) * 100;
            const angle = (item.count / total) * 360;
            const startAngle = currentAngle;
            const endAngle = currentAngle + angle;

            currentAngle = endAngle;

            const x1 = center + radius * Math.cos((startAngle - 90) * Math.PI / 180);
            const y1 = center + radius * Math.sin((startAngle - 90) * Math.PI / 180);
            const x2 = center + radius * Math.cos((endAngle - 90) * Math.PI / 180);
            const y2 = center + radius * Math.sin((endAngle - 90) * Math.PI / 180);

            const largeArc = angle > 180 ? 1 : 0;

            const pathData = [
              `M ${center} ${center}`,
              `L ${x1} ${y1}`,
              `A ${radius} ${radius} 0 ${largeArc} 1 ${x2} ${y2}`,
              'Z'
            ].join(' ');

            const colors = ['#3b82f6', '#8b5cf6', '#ec4899', '#f59e0b', '#10b981'];
            const color = colors[index % colors.length];

            return (
              <g key={index}>
                <path d={pathData} fill={color} opacity={0.8} />
              </g>
            );
          })}
        </svg>
        <div className="pie-legend">
          {data.map((item, index) => {
            const colors = ['#3b82f6', '#8b5cf6', '#ec4899', '#f59e0b', '#10b981'];
            const color = colors[index % colors.length];
            const percentage = ((item.count / total) * 100).toFixed(1);

            return (
              <div key={index} className="legend-item">
                <div className="legend-color" style={{ backgroundColor: color }}></div>
                <div className="legend-label">{item.category}</div>
                <div className="legend-value">{percentage}%</div>
              </div>
            );
          })}
        </div>
      </div>
    );
  };

  const renderLineChart = (data) => {
    if (!data || data.length === 0) return null;

    const maxValue = Math.max(...data.map(d => d.event_count));
    const width = 600;
    const height = 200;
    const padding = 40;

    const points = data.map((d, i) => {
      const x = padding + (i / (data.length - 1)) * (width - 2 * padding);
      const y = height - padding - ((d.event_count / maxValue) * (height - 2 * padding));
      return `${x},${y}`;
    }).join(' ');

    return (
      <div className="line-chart-container">
        <svg width={width} height={height} className="line-chart">
          {/* Grid lines */}
          {[0, 0.25, 0.5, 0.75, 1].map((ratio, i) => (
            <line
              key={i}
              x1={padding}
              y1={height - padding - ratio * (height - 2 * padding)}
              x2={width - padding}
              y2={height - padding - ratio * (height - 2 * padding)}
              stroke="#e5e7eb"
              strokeWidth="1"
            />
          ))}

          {/* Line */}
          <polyline
            points={points}
            fill="none"
            stroke="#3b82f6"
            strokeWidth="3"
            strokeLinecap="round"
            strokeLinejoin="round"
          />

          {/* Points */}
          {data.map((d, i) => {
            const x = padding + (i / (data.length - 1)) * (width - 2 * padding);
            const y = height - padding - ((d.event_count / maxValue) * (height - 2 * padding));
            return (
              <circle
                key={i}
                cx={x}
                cy={y}
                r="4"
                fill="#3b82f6"
              />
            );
          })}

          {/* Trend line */}
          {trendAnalysis?.trend && (
            <line
              x1={padding}
              y1={height - padding - ((trendAnalysis.trend.line[0].y / maxValue) * (height - 2 * padding))}
              x2={width - padding}
              y2={height - padding - ((trendAnalysis.trend.line[trendAnalysis.trend.line.length - 1].y / maxValue) * (height - 2 * padding))}
              stroke="#f59e0b"
              strokeWidth="2"
              strokeDasharray="5,5"
            />
          )}
        </svg>
        {trendAnalysis?.trend && (
          <div className="trend-info">
            Trend: <span className={`trend-${trendAnalysis.trend.direction}`}>
              {trendAnalysis.trend.direction}
            </span>
          </div>
        )}
      </div>
    );
  };

  const renderTagCloud = () => {
    if (!tagCloud || tagCloud.length === 0) return null;

    const maxCount = Math.max(...tagCloud.map(t => t.usage_count));
    const minSize = 0.75;
    const maxSize = 2.5;

    return (
      <div className="tag-cloud">
        {tagCloud.map((tag, index) => {
          const size = minSize + ((tag.usage_count / maxCount) * (maxSize - minSize));
          return (
            <span
              key={index}
              className="cloud-tag"
              style={{
                fontSize: `${size}rem`,
                color: tag.color || '#3b82f6'
              }}
            >
              {tag.name}
            </span>
          );
        })}
      </div>
    );
  };

  if (loading) {
    return (
      <div className="analytics-dashboard loading">
        <div className="loading-spinner">Loading analytics...</div>
      </div>
    );
  }

  return (
    <div className="analytics-dashboard">
      <div className="dashboard-header">
        <h2>Analytics Dashboard</h2>
        {onClose && (
          <button className="close-btn" onClick={onClose}>×</button>
        )}
      </div>

      <div className="dashboard-tabs">
        <button
          className={`tab ${activeTab === 'overview' ? 'active' : ''}`}
          onClick={() => setActiveTab('overview')}
        >
          Overview
        </button>
        <button
          className={`tab ${activeTab === 'timeline' ? 'active' : ''}`}
          onClick={() => setActiveTab('timeline')}
        >
          Timeline
        </button>
        <button
          className={`tab ${activeTab === 'categories' ? 'active' : ''}`}
          onClick={() => setActiveTab('categories')}
        >
          Categories
        </button>
        <button
          className={`tab ${activeTab === 'tags' ? 'active' : ''}`}
          onClick={() => setActiveTab('tags')}
        >
          Tags
        </button>
        <button
          className={`tab ${activeTab === 'eras' ? 'active' : ''}`}
          onClick={() => setActiveTab('eras')}
        >
          Eras
        </button>
      </div>

      <div className="dashboard-content">
        {activeTab === 'overview' && stats && (
          <div className="overview-tab">
            <div className="stats-grid">
              <div className="stat-card">
                <div className="stat-icon">📊</div>
                <div className="stat-value">{stats.totalEvents}</div>
                <div className="stat-label">Total Events</div>
              </div>
              <div className="stat-card">
                <div className="stat-icon">🏷️</div>
                <div className="stat-value">{stats.totalTags}</div>
                <div className="stat-label">Tags</div>
              </div>
              <div className="stat-card">
                <div className="stat-icon">👥</div>
                <div className="stat-value">{stats.totalPeople}</div>
                <div className="stat-label">People</div>
              </div>
              <div className="stat-card">
                <div className="stat-icon">📍</div>
                <div className="stat-value">{stats.totalLocations}</div>
                <div className="stat-label">Locations</div>
              </div>
              <div className="stat-card">
                <div className="stat-icon">🎭</div>
                <div className="stat-value">{stats.totalEras}</div>
                <div className="stat-label">Eras</div>
              </div>
              <div className="stat-card">
                <div className="stat-icon">📅</div>
                <div className="stat-value">{stats.timelineSpanDays || 0}</div>
                <div className="stat-label">Days Covered</div>
              </div>
            </div>

            <div className="insights-section">
              <h3>Key Insights</h3>
              <div className="insights-grid">
                {stats.mostActiveCategory && (
                  <div className="insight-card">
                    <div className="insight-label">Most Active Category</div>
                    <div className="insight-value">{stats.mostActiveCategory.category}</div>
                    <div className="insight-count">{stats.mostActiveCategory.count} events</div>
                  </div>
                )}
                {stats.mostUsedTag && (
                  <div className="insight-card">
                    <div className="insight-label">Most Used Tag</div>
                    <div className="insight-value">{stats.mostUsedTag.name}</div>
                    <div className="insight-count">{stats.mostUsedTag.count} times</div>
                  </div>
                )}
                {stats.mostReferencedPerson && (
                  <div className="insight-card">
                    <div className="insight-label">Most Referenced Person</div>
                    <div className="insight-value">{stats.mostReferencedPerson.name}</div>
                    <div className="insight-count">{stats.mostReferencedPerson.count} events</div>
                  </div>
                )}
                {stats.avgEventsPerMonth && (
                  <div className="insight-card">
                    <div className="insight-label">Avg Events Per Month</div>
                    <div className="insight-value">{stats.avgEventsPerMonth}</div>
                    <div className="insight-count">Based on timeline span</div>
                  </div>
                )}
              </div>
            </div>

            {stats.earliestEvent && stats.latestEvent && (
              <div className="timeline-info">
                <h3>Timeline Range</h3>
                <div className="date-range">
                  <div className="date-item">
                    <div className="date-label">Earliest Event</div>
                    <div className="date-value">
                      {new Date(stats.earliestEvent).toLocaleDateString()}
                    </div>
                  </div>
                  <div className="date-separator">→</div>
                  <div className="date-item">
                    <div className="date-label">Latest Event</div>
                    <div className="date-value">
                      {new Date(stats.latestEvent).toLocaleDateString()}
                    </div>
                  </div>
                </div>
              </div>
            )}
          </div>
        )}

        {activeTab === 'timeline' && (
          <div className="timeline-tab">
            <div className="tab-header">
              <h3>Event Distribution Over Time</h3>
              <select
                value={timeGrouping}
                onChange={(e) => setTimeGrouping(e.target.value)}
                className="grouping-select"
              >
                <option value="day">Daily</option>
                <option value="week">Weekly</option>
                <option value="month">Monthly</option>
                <option value="year">Yearly</option>
              </select>
            </div>
            {trendAnalysis && renderLineChart(trendAnalysis.data)}
          </div>
        )}

        {activeTab === 'categories' && (
          <div className="categories-tab">
            <h3>Category Distribution</h3>
            {categoryData.length > 0 && (
              <>
                <div className="chart-section">
                  {renderPieChart(categoryData)}
                </div>
                <div className="chart-section">
                  {renderBarChart(categoryData, 'count', 'category')}
                </div>
              </>
            )}
          </div>
        )}

        {activeTab === 'tags' && (
          <div className="tags-tab">
            <h3>Tag Cloud</h3>
            <p className="tab-description">
              Tag size represents frequency of use
            </p>
            {renderTagCloud()}
          </div>
        )}

        {activeTab === 'eras' && (
          <div className="eras-tab">
            <h3>Era Statistics</h3>
            {eraStats.length > 0 ? (
              <div className="eras-list">
                {eraStats.map((era) => (
                  <div key={era.id} className="era-card">
                    <div
                      className="era-color"
                      style={{ backgroundColor: era.color }}
                    ></div>
                    <div className="era-content">
                      <h4>{era.name}</h4>
                      <div className="era-stats">
                        <div className="era-stat">
                          <span className="era-stat-label">Events:</span>
                          <span className="era-stat-value">{era.event_count}</span>
                        </div>
                        <div className="era-stat">
                          <span className="era-stat-label">Categories:</span>
                          <span className="era-stat-value">{era.category_count}</span>
                        </div>
                        <div className="era-stat">
                          <span className="era-stat-label">Unique Tags:</span>
                          <span className="era-stat-value">{era.unique_tags}</span>
                        </div>
                        <div className="era-stat">
                          <span className="era-stat-label">People:</span>
                          <span className="era-stat-value">{era.unique_people}</span>
                        </div>
                      </div>
                      {era.start_date && era.end_date && (
                        <div className="era-dates">
                          {new Date(era.start_date).toLocaleDateString()} - {new Date(era.end_date).toLocaleDateString()}
                        </div>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              <div className="no-data">No eras defined</div>
            )}
          </div>
        )}
      </div>
    </div>
  );
};

export default AnalyticsDashboard;
