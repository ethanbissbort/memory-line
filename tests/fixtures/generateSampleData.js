#!/usr/bin/env node
/**
 * Generate Sample Data Script
 * CLI tool for generating sample JSON data files
 */

const fs = require('fs');
const path = require('path');
const { generateSampleData } = require('./sampleDataGenerator');

// Parse command line arguments
const args = process.argv.slice(2);
const eventCount = parseInt(args[0]) || 100;
const outputFile = args[1] || `sample-data-${eventCount}.json`;

console.log(`\n📊 Generating Sample Data\n`);
console.log(`Events: ${eventCount}`);
console.log(`Output: ${outputFile}\n`);

try {
    // Generate data
    const data = generateSampleData(eventCount);

    // Ensure output directory exists
    const outputDir = path.dirname(outputFile);
    if (outputDir !== '.' && !fs.existsSync(outputDir)) {
        fs.mkdirSync(outputDir, { recursive: true });
    }

    // Write to file
    fs.writeFileSync(outputFile, JSON.stringify(data, null, 2));

    console.log('✅ Sample data generated successfully!\n');
    console.log('Statistics:');
    console.log(`  Events: ${data.statistics.total_events}`);
    console.log(`  Eras: ${data.statistics.total_eras}`);
    console.log(`  Tags: ${data.statistics.total_tags}`);
    console.log(`  People: ${data.statistics.total_people}`);
    console.log(`  Locations: ${data.statistics.total_locations}\n`);
    console.log(`  File: ${outputFile}`);
    console.log(`  Size: ${(fs.statSync(outputFile).size / 1024).toFixed(2)} KB\n`);

} catch (error) {
    console.error('❌ Error generating sample data:', error.message);
    process.exit(1);
}
