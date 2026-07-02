/**
 * Event Bubble Component
 * Visual representation of an event on the timeline
 */

import React, { useState } from 'react';
import { calculateDatePosition, getDurationDays } from '../../utils/timelineUtils';
import { format, parseISO, isValid } from 'date-fns';

// Category icon mapping
const getCategoryIcon = (category) => {
    const icons = {
        milestone: '🏆',
        work: '💼',
        education: '🎓',
        relationship: '❤️',
        travel: '✈️',
        achievement: '⭐',
        challenge: '⚡',
        era: '📅',
        health: '🏥',
        hobby: '🎨',
        family: '👨‍👩‍👧‍👦',
        social: '🎉',
        financial: '💰',
        personal: '👤',
        other: '📌'
    };
    return icons[category] || '📌';
};

function EventBubble({ event, timelineWidth, zoomLevel, currentViewDate, panOffset = 0, onClick = () => {} }) {
    const [isHovered, setIsHovered] = useState(false);

    const startDate = event.start_date ? parseISO(event.start_date) : null;
    const parsedEnd = event.end_date ? parseISO(event.end_date) : null;
    const endDate = parsedEnd && isValid(parsedEnd) ? parsedEnd : null;

    // Guard against missing/invalid dates so a bad record doesn't throw in render
    if (!startDate || !isValid(startDate)) {
        return null;
    }

    // Calculate position on timeline
    const position = calculateDatePosition(startDate, currentViewDate, zoomLevel, timelineWidth);

    // Calculate width based on duration and zoom level
    const duration = endDate ? getDurationDays(startDate, endDate) : 1;
    const zoomMultiplier = {
        year: 0.5,
        month: 1.5,
        week: 3,
        day: 10
    }[zoomLevel] || 1;

    const minWidth = {
        year: 50,
        month: 70,
        week: 90,
        day: 120
    }[zoomLevel] || 60;

    const width = Math.max(minWidth, duration * zoomMultiplier);

    // Determine if event is in view. The parent applies panOffset as a
    // translateX, so account for it here to cull against the visible viewport.
    const viewportPosition = position + panOffset;
    if (viewportPosition < -width || viewportPosition > timelineWidth) {
        return null; // Don't render if out of view
    }

    // Determine height based on whether it's a duration event
    const height = endDate ? 60 : 50;

    // Format date display based on zoom level
    const dateFormat = {
        year: 'MMM yyyy',
        month: 'MMM d',
        week: 'MMM d',
        day: 'MMM d, h:mm a'
    }[zoomLevel] || 'MMM d, yyyy';

    return (
        <div
            className={`event-bubble ${endDate ? 'duration-event' : 'point-event'} ${isHovered ? 'hovered' : ''}`}
            style={{
                left: `${position}px`,
                width: `${width}px`,
                height: `${height}px`,
                backgroundColor: event.era_color || '#3498db'
            }}
            onClick={onClick}
            onMouseEnter={() => setIsHovered(true)}
            onMouseLeave={() => setIsHovered(false)}
            title={event.description || event.title}
        >
            <div className="event-bubble-content">
                <div className="event-header">
                    <span className="event-icon">{getCategoryIcon(event.category)}</span>
                    <div className="event-title">{event.title}</div>
                </div>
                <div className="event-date">{format(startDate, dateFormat)}</div>
                {endDate && (
                    <div className="event-duration">→ {format(endDate, dateFormat)}</div>
                )}
            </div>

            {/* Hover tooltip */}
            {isHovered && (
                <div className="event-tooltip">
                    <strong>{event.title}</strong>
                    <div>{format(startDate, 'MMMM d, yyyy')}</div>
                    {endDate && <div>to {format(endDate, 'MMMM d, yyyy')}</div>}
                    {event.category && <div className="tooltip-category">{event.category}</div>}
                    {event.description && (
                        <div className="tooltip-description">
                            {event.description.substring(0, 100)}
                            {event.description.length > 100 && '...'}
                        </div>
                    )}
                </div>
            )}
        </div>
    );
}

export default EventBubble;
