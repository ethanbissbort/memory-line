# Memory Timeline - Test Suite

> ## ⚠️ LEGACY — Electron implementation (status as of 2026-07-10)
>
> This test suite covers the **legacy Electron/Node.js** version of Memory Timeline (Jest + better-sqlite3).
> The **primary, actively developed product is now the Windows Native app (.NET 8 / WinUI 3)** under [`windows-native/`](../windows-native/).
> For the primary test suite, see [`windows-native/TESTING.md`](../windows-native/TESTING.md).
>
> The content below remains valid for the Electron app.

Comprehensive testing infrastructure for the Memory Timeline application.

## Overview

This test suite includes:
- **Unit Tests**: Service-level tests for all core functionality
- **Sample Data Generators**: Realistic data for testing
- **Database Seeders**: Pre-populated test databases
- **Performance Tests**: Load and stress testing

## Quick Start

```bash
# Install dependencies first
npm install

# Run all tests
npm test

# Run with coverage report
npm run test:coverage

# Watch mode for development
npm run test:watch

# Run only service tests
npm run test:services
```

## Test Structure

```
tests/
├── fixtures/
│   ├── databases/           # Pre-generated test databases
│   │   ├── test-tiny.db    # 10 events
│   │   ├── test-small.db   # 100 events
│   │   ├── test-medium.db  # 500 events
│   │   ├── test-large.db   # 1,000 events
│   │   └── test-stress.db  # 5,000+ events
│   ├── sampleDataGenerator.js  # Generate realistic sample data
│   ├── databaseSeeder.js       # Seed databases with data
│   └── generateSampleData.js   # CLI tool for data generation
├── services/
│   ├── exportService.test.js      # Export/import tests
│   ├── performanceService.test.js # Performance tests
│   ├── embeddingService.test.js   # Embedding tests
│   └── ragService.test.js         # RAG tests
├── performance/
│   └── performanceTest.js  # Load and performance benchmarks
├── setup.js               # Jest test setup
└── README.md             # This file
```

## Test Databases

### Generating Test Databases

```bash
# Generate all test databases
npm run test:seed
```

This creates 5 databases with varying sizes:

| Database | Events | Size | Use Case |
|----------|--------|------|----------|
| `test-tiny.db` | 10 | ~100 KB | Quick smoke tests |
| `test-small.db` | 100 | ~500 KB | Unit tests |
| `test-medium.db` | 500 | ~2 MB | Integration tests |
| `test-large.db` | 1,000 | ~5 MB | Performance tests |
| `test-stress.db` | 5,000+ | ~20 MB | Stress tests |

### Using Test Databases

```javascript
const Database = require('better-sqlite3');
const path = require('path');

const db = new Database(path.join(__dirname, 'fixtures/databases/test-small.db'));
const events = db.prepare('SELECT * FROM events LIMIT 10').all();
```

## Sample Data Generation

### Generate JSON Sample Data

```bash
# Generate 100 events
npm run test:generate-data

# Generate custom amount
node tests/fixtures/generateSampleData.js 500 output/sample-500.json
```

### Programmatic Usage

```javascript
const { generateSampleData } = require('./fixtures/sampleDataGenerator');

const data = generateSampleData(100, 5); // 100 events, 5 eras

console.log(data.statistics);
// {
//   total_events: 100,
//   total_eras: 5,
//   total_tags: 20,
//   total_people: 45,
//   total_locations: 18
// }
```

## Unit Tests

### Running Specific Test Suites

```bash
# All service tests
npm run test:services

# Specific service
npx jest tests/services/exportService.test.js

# With verbose output
npm run test:verbose
```

### Test Coverage

```bash
# Generate coverage report
npm run test:coverage

# View HTML report
open coverage/lcov-report/index.html
```

**Coverage Goals:**
- Overall: > 80%
- Services: > 90%
- Critical paths: 100%

## Performance Tests

### Running Performance Tests

```bash
npm run test:performance
```

### Performance Benchmarks

Expected performance on different database sizes:

| Test | Small (100) | Medium (500) | Large (1K) | Stress (5K) |
|------|-------------|--------------|------------|-------------|
| Full Scan | < 5ms | < 20ms | < 50ms | < 200ms |
| Indexed Query | < 2ms | < 5ms | < 10ms | < 30ms |
| Paginated Query | < 10ms | < 20ms | < 30ms | < 100ms |
| Cached Query | < 1ms | < 2ms | < 5ms | < 10ms |
| FTS Search | < 5ms | < 10ms | < 20ms | < 50ms |

### Performance Test Output

```
📊 Testing: test-stress
──────────────────────────────────────────────────
Events: 5000
Full Scan: 142.35ms (5000 rows)
Indexed Query: 8.21ms (234 rows)
Paginated Query: 24.67ms (100 rows)
Cached Query: 0.87ms (100 rows)
Cache speedup: 28.4x
Full-text Search: 12.43ms (50 rows)
Join Query: 31.22ms (100 rows)
Batch Load Relationships: 6.54ms (100 events)
```

## Writing Tests

### Test Template

```javascript
const Database = require('better-sqlite3');
const ServiceName = require('../../src/main/services/serviceName');

describe('ServiceName', () => {
    let db;
    let service;

    beforeEach(() => {
        db = new Database(':memory:');
        // Create schema
        db.exec(`CREATE TABLE ...`);
        service = new ServiceName(db);
    });

    afterEach(() => {
        db.close();
    });

    describe('methodName', () => {
        test('should do something', () => {
            const result = service.methodName();
            expect(result).toBeDefined();
        });
    });
});
```

### Best Practices

1. **Use In-Memory Databases**: Faster and isolated
2. **Clean Up**: Always close databases in `afterEach`
3. **Test Errors**: Include error path testing
4. **Mock External APIs**: Don't make real API calls
5. **Descriptive Names**: Test names should explain intent

## Continuous Integration

### GitHub Actions Example

```yaml
name: Tests
on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v2
      - uses: actions/setup-node@v2
        with:
          node-version: '18'
      - run: npm install
      - run: npm run test:all
      - uses: codecov/codecov-action@v2
```

## Troubleshooting

### Common Issues

**1. Module not found errors**
```bash
npm install
```

**2. Database locked errors**
```bash
# Close all database connections
# Or use in-memory databases for tests
```

**3. Test timeouts**
```javascript
// Increase timeout in jest.config.js
testTimeout: 10000
```

**4. Missing test databases**
```bash
npm run test:seed
```

### Debug Mode

```bash
# Run Jest with Node inspector
node --inspect-brk node_modules/.bin/jest --runInBand

# Verbose output
npm run test:verbose
```

## Test Data

### Sample Event Structure

```json
{
  "event_id": "uuid",
  "title": "Graduated from Computer Science",
  "start_date": "2020-05-15",
  "end_date": null,
  "description": "This was a significant moment...",
  "category": "education",
  "era_id": "era-uuid",
  "tags": ["milestone", "achievement"],
  "people": ["John Doe", "Jane Smith"],
  "locations": ["New York"]
}
```

### Realistic Data Features

- **30+ event categories**: work, education, milestone, travel, etc.
- **100+ name variations**: First/last name combinations
- **50+ location options**: Major cities and regions
- **20+ tag types**: From common to specific
- **Date distribution**: Realistic spread across years
- **Relationships**: Events with appropriate tags, people, locations

## Performance Optimization Tips

1. **Use Pagination**: Don't load all events at once
2. **Enable Caching**: Use PerformanceService caching
3. **Batch Operations**: Load relationships in batches
4. **Index Properly**: Ensure critical columns are indexed
5. **Regular VACUUM**: Keep database optimized

## Contributing Tests

When adding new features:

1. Add unit tests in `tests/services/`
2. Update sample data generator if needed
3. Add performance benchmarks
4. Update this README
5. Ensure > 80% coverage

## Resources

- [Jest Documentation](https://jestjs.io/)
- [better-sqlite3 Documentation](https://github.com/WiseLibs/better-sqlite3)
- [Testing Best Practices](https://github.com/goldbergyoni/javascript-testing-best-practices)

## License

MIT - Same as main project
