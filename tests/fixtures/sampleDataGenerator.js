/**
 * Sample Data Generator
 * Generates realistic sample events, eras, tags, people, and locations
 */

const { v4: uuidv4 } = require('uuid');

// Sample data pools
const firstNames = [
    'James', 'Mary', 'John', 'Patricia', 'Robert', 'Jennifer', 'Michael', 'Linda',
    'William', 'Barbara', 'David', 'Elizabeth', 'Richard', 'Susan', 'Joseph', 'Jessica',
    'Thomas', 'Sarah', 'Charles', 'Karen', 'Christopher', 'Nancy', 'Daniel', 'Lisa'
];

const lastNames = [
    'Smith', 'Johnson', 'Williams', 'Brown', 'Jones', 'Garcia', 'Miller', 'Davis',
    'Rodriguez', 'Martinez', 'Hernandez', 'Lopez', 'Gonzalez', 'Wilson', 'Anderson', 'Thomas'
];

const cities = [
    'New York', 'Los Angeles', 'Chicago', 'Houston', 'Phoenix', 'Philadelphia',
    'San Antonio', 'San Diego', 'Dallas', 'San Jose', 'Austin', 'Jacksonville',
    'San Francisco', 'Seattle', 'Denver', 'Boston', 'Portland', 'Miami', 'Atlanta'
];

const tags = [
    'important', 'memorable', 'turning-point', 'first-time', 'celebration', 'challenge',
    'achievement', 'learning', 'growth', 'career', 'family', 'friends', 'health',
    'travel', 'hobby', 'education', 'romance', 'adventure', 'milestone', 'difficult'
];

const eventTitles = {
    milestone: [
        'Graduated from {}',
        'Started first job at {}',
        'Bought first house in {}',
        'Got married to {}',
        'Had first child',
        'Got promoted to {}',
        'Moved to {}',
        'Started own business',
        'Retired from {}',
        'Celebrated {} anniversary'
    ],
    work: [
        'Started new position at {}',
        'Completed major project: {}',
        'Attended conference in {}',
        'Received award for {}',
        'Changed careers to {}',
        'Launched product: {}',
        'Team building event at {}',
        'Professional certification in {}'
    ],
    education: [
        'Enrolled in {} course',
        'Graduated with degree in {}',
        'Attended workshop on {}',
        'Completed certification in {}',
        'Started masters program in {}',
        'Published research on {}',
        'Presented at {} conference',
        'Learned new skill: {}'
    ],
    relationship: [
        'Met {} for first time',
        'Started dating {}',
        'Got engaged to {}',
        'Married {}',
        'Anniversary celebration with {}',
        'Family reunion in {}',
        'Rekindled friendship with {}',
        'Welcomed {} to family'
    ],
    travel: [
        'Visited {} for first time',
        'Road trip to {}',
        'Vacation in {}',
        'Backpacking through {}',
        'Weekend getaway to {}',
        'International trip to {}',
        'Explored {} with {}',
        'Adventure in {}'
    ],
    achievement: [
        'Won award for {}',
        'Completed marathon',
        'Published book: {}',
        'Reached goal of {}',
        'Mastered {}',
        'Created {}',
        'Built {}',
        'Accomplished {}'
    ],
    challenge: [
        'Overcame difficulty with {}',
        'Dealt with health issue',
        'Recovered from {}',
        'Persevered through {}',
        'Bounced back from {}',
        'Survived {}',
        'Endured {}',
        'Conquered fear of {}'
    ]
};

const descriptions = [
    'This was a significant moment in my life that shaped who I am today.',
    'Looking back, this event taught me valuable lessons about perseverance and growth.',
    'This experience brought me closer to the people I care about most.',
    'I remember this day vividly - it changed my perspective on many things.',
    'This was a challenging time, but I grew stronger because of it.',
    'One of the best decisions I ever made, leading to so many good things.',
    'This memory always brings a smile to my face when I think about it.',
    'A turning point that opened new doors and opportunities.',
    'Though difficult at the time, this experience was ultimately rewarding.',
    'This marked the beginning of an exciting new chapter in my life.'
];

/**
 * Generate random date between start and end
 */
function randomDate(start, end) {
    return new Date(start.getTime() + Math.random() * (end.getTime() - start.getTime()));
}

/**
 * Format date as YYYY-MM-DD
 */
function formatDate(date) {
    return date.toISOString().split('T')[0];
}

/**
 * Pick random item from array
 */
function randomItem(array) {
    return array[Math.floor(Math.random() * array.length)];
}

/**
 * Pick random items from array
 */
function randomItems(array, count) {
    const shuffled = [...array].sort(() => 0.5 - Math.random());
    return shuffled.slice(0, count);
}

/**
 * Generate random person name
 */
function generatePersonName() {
    return `${randomItem(firstNames)} ${randomItem(lastNames)}`;
}

/**
 * Generate sample eras
 */
function generateEras(count = 5) {
    const eraNames = [
        'Childhood',
        'High School Years',
        'College Years',
        'Early Career',
        'Mid Career',
        'Starting a Family',
        'Growth Phase',
        'Established Career',
        'Pre-Retirement',
        'Retirement Years'
    ];

    const colors = [
        '#e74c3c', '#3498db', '#2ecc71', '#f39c12', '#9b59b6',
        '#1abc9c', '#34495e', '#e67e22', '#16a085', '#c0392b'
    ];

    const startYear = 1990;
    const yearsPerEra = 30 / count;

    return Array.from({ length: count }, (_, i) => {
        const startDate = new Date(startYear + i * yearsPerEra, 0, 1);
        const endDate = i === count - 1 ? null : new Date(startYear + (i + 1) * yearsPerEra, 0, 1);

        return {
            era_id: uuidv4(),
            name: eraNames[i % eraNames.length],
            start_date: formatDate(startDate),
            end_date: endDate ? formatDate(endDate) : null,
            color_code: colors[i % colors.length],
            description: `This era represents ${eraNames[i % eraNames.length].toLowerCase()}`
        };
    });
}

/**
 * Generate sample events
 */
function generateEvents(count, eras = []) {
    const categories = Object.keys(eventTitles);
    const events = [];

    for (let i = 0; i < count; i++) {
        const category = randomItem(categories);
        const titleTemplate = randomItem(eventTitles[category]);

        // Generate random date between 1990 and now
        const startDate = randomDate(new Date(1990, 0, 1), new Date());

        // 30% chance of having end date (duration event)
        const hasDuration = Math.random() < 0.3;
        const endDate = hasDuration
            ? new Date(startDate.getTime() + Math.random() * 90 * 24 * 60 * 60 * 1000) // 0-90 days
            : null;

        // Fill in title template
        const title = titleTemplate.replace('{}',
            category === 'travel' ? randomItem(cities) :
            category === 'relationship' ? generatePersonName() :
            category === 'education' ? randomItem(['Computer Science', 'Data Science', 'Business', 'Marketing', 'Psychology']) :
            'various things'
        );

        // Find appropriate era
        let eraId = null;
        if (eras.length > 0) {
            const era = eras.find(e => {
                const eraStart = new Date(e.start_date);
                const eraEnd = e.end_date ? new Date(e.end_date) : new Date();
                return startDate >= eraStart && startDate <= eraEnd;
            });
            if (era) eraId = era.era_id;
        }

        events.push({
            event_id: uuidv4(),
            title: title,
            start_date: formatDate(startDate),
            end_date: endDate ? formatDate(endDate) : null,
            description: randomItem(descriptions),
            category: category,
            era_id: eraId,
            tags: randomItems(tags, Math.floor(Math.random() * 4) + 1),
            people: Array.from({ length: Math.floor(Math.random() * 3) }, () => generatePersonName()),
            locations: randomItems(cities, Math.floor(Math.random() * 2) + 1)
        });
    }

    // Sort by date
    return events.sort((a, b) => new Date(a.start_date) - new Date(b.start_date));
}

/**
 * Generate sample data package
 */
function generateSampleData(eventCount = 100, eraCount = 5) {
    const eras = generateEras(eraCount);
    const events = generateEvents(eventCount, eras);

    // Extract unique tags, people, locations
    const tagsSet = new Set();
    const peopleSet = new Set();
    const locationsSet = new Set();

    events.forEach(event => {
        event.tags.forEach(tag => tagsSet.add(tag));
        event.people.forEach(person => peopleSet.add(person));
        event.locations.forEach(location => locationsSet.add(location));
    });

    return {
        version: '1.0.0',
        generated_at: new Date().toISOString(),
        event_count: events.length,
        events: events,
        eras: eras,
        tags: Array.from(tagsSet).map(name => ({
            tag_id: uuidv4(),
            tag_name: name
        })),
        people: Array.from(peopleSet).map(name => ({
            person_id: uuidv4(),
            name: name
        })),
        locations: Array.from(locationsSet).map(name => ({
            location_id: uuidv4(),
            name: name
        })),
        statistics: {
            total_events: events.length,
            total_eras: eras.length,
            total_tags: tagsSet.size,
            total_people: peopleSet.size,
            total_locations: locationsSet.size,
            events_by_category: Object.keys(eventTitles).reduce((acc, cat) => {
                acc[cat] = events.filter(e => e.category === cat).length;
                return acc;
            }, {})
        }
    };
}

module.exports = {
    generateSampleData,
    generateEvents,
    generateEras,
    generatePersonName
};
