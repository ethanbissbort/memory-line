/**
 * Event Bubble Component
 * Visual representation of an event on the timeline
 */

import React from 'react';
import { calculateDatePosition, getDurationDays } from '../../utils/timelineUtils';
import { format, parseISO } from 'date-fns';

function EventBubble({ event, timelineWidth, zoomLevel, currentViewDate, onClick }) {
    const startDate = parseISO(event.start_date);
    const endDate = event.end_date ? parseISO(event.end_date) : null;

    // Calculate position on timeline
    const position = calculateDatePosition(startDate, currentViewDate, zoomLevel, timelineWidth);

    // Calculate width based on duration (minimum width for point events)
    const duration = endDate ? getDurationDays(startDate, endDate) : 1;
    const width = Math.max(60, duration * 2); // Adjust based on zoom level

    // Determine if event is in view
    if (position < -width || position > timelineWidth) {
        return null; // Don't render if out of view
    }

    return (
        <div
            className="event-bubble"
            style={{
                left: `${position}px`,
                width: `${width}px`,
                backgroundColor: event.era_color || '#3498db'
            }}
            onClick={onClick}
            title={event.title}
        >
            <div className="event-bubble-content">
                <div className="event-title">{event.title}</div>
                <div className="event-date">{format(startDate, 'MMM d, yyyy')}</div>
                {event.category && (
                    <div className="event-category">{event.category}</div>
                )}
            </div>
        </div>
    );
}

export default EventBubble;
