/**
 * Timeline Utility Functions
 * Helper functions for date calculations and positioning
 */

import { differenceInDays, startOfYear, endOfYear, startOfMonth, endOfMonth, startOfWeek, endOfWeek, addDays } from 'date-fns';

/**
 * Calculate the pixel position of a date on the timeline
 * @param {Date} date - The date to position
 * @param {Date} viewDate - The current view center date
 * @param {string} zoomLevel - year, month, week, or day
 * @param {number} timelineWidth - Width of the timeline in pixels
 * @returns {number} Pixel position
 */
export function calculateDatePosition(date, viewDate, zoomLevel, timelineWidth) {
    const { start, end } = getDateRangeForZoom(viewDate, zoomLevel);

    const totalDays = differenceInDays(end, start);
    const daysFromStart = differenceInDays(date, start);

    const pixelsPerDay = timelineWidth / totalDays;
    return daysFromStart * pixelsPerDay;
}

/**
 * Get the date range to display based on zoom level
 * @param {Date} centerDate - The center date of the view
 * @param {string} zoomLevel - year, month, week, or day
 * @returns {Object} { start: Date, end: Date }
 */
export function getDateRangeForZoom(centerDate, zoomLevel) {
    let start, end;

    switch (zoomLevel) {
        case 'year':
            // Show 3 years: previous, current, next
            start = startOfYear(new Date(centerDate.getFullYear() - 1, 0, 1));
            end = endOfYear(new Date(centerDate.getFullYear() + 1, 11, 31));
            break;

        case 'month':
            // Show 3 months: previous, current, next
            const prevMonth = new Date(centerDate);
            prevMonth.setMonth(prevMonth.getMonth() - 1);
            const nextMonth = new Date(centerDate);
            nextMonth.setMonth(nextMonth.getMonth() + 1);

            start = startOfMonth(prevMonth);
            end = endOfMonth(nextMonth);
            break;

        case 'week':
            // Show 3 weeks
            const prevWeek = addDays(centerDate, -7);
            const nextWeek = addDays(centerDate, 7);

            start = startOfWeek(prevWeek);
            end = endOfWeek(nextWeek);
            break;

        case 'day':
            // Show 7 days
            start = addDays(centerDate, -3);
            end = addDays(centerDate, 3);
            break;

        default:
            // Default to month view
            start = startOfMonth(centerDate);
            end = endOfMonth(centerDate);
    }

    return {
        start: start.toISOString().split('T')[0],
        end: end.toISOString().split('T')[0]
    };
}

/**
 * Calculate duration in days between two dates
 * @param {Date} startDate
 * @param {Date} endDate
 * @returns {number} Duration in days
 */
export function getDurationDays(startDate, endDate) {
    return differenceInDays(endDate, startDate);
}

/**
 * Generate date markers for the timeline axis
 * @param {Date} startDate
 * @param {Date} endDate
 * @param {string} zoomLevel
 * @returns {Array} Array of date markers
 */
export function generateDateMarkers(startDate, endDate, zoomLevel) {
    const markers = [];
    const totalDays = differenceInDays(endDate, startDate);

    let interval;
    switch (zoomLevel) {
        case 'year':
            interval = 30; // Monthly markers
            break;
        case 'month':
            interval = 7; // Weekly markers
            break;
        case 'week':
            interval = 1; // Daily markers
            break;
        case 'day':
            interval = 1; // Daily markers
            break;
        default:
            interval = 7;
    }

    for (let i = 0; i <= totalDays; i += interval) {
        markers.push(addDays(startDate, i));
    }

    return markers;
}

/**
 * Format date for display based on zoom level
 * @param {Date} date
 * @param {string} zoomLevel
 * @returns {string} Formatted date string
 */
export function formatDateForZoom(date, zoomLevel) {
    switch (zoomLevel) {
        case 'year':
            return date.toLocaleDateString('en-US', { month: 'short', year: 'numeric' });
        case 'month':
            return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
        case 'week':
        case 'day':
            return date.toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric' });
        default:
            return date.toLocaleDateString();
    }
}
