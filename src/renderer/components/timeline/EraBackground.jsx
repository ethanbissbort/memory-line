/**
 * Era Background Component
 * Colored background bands for life eras
 */

import React from 'react';
import { calculateDatePosition } from '../../utils/timelineUtils';
import { parseISO, differenceInDays } from 'date-fns';

function EraBackground({ era, timelineWidth, zoomLevel, currentViewDate }) {
    const startDate = parseISO(era.start_date);
    const endDate = era.end_date ? parseISO(era.end_date) : new Date();

    // Calculate position and width
    const startPos = calculateDatePosition(startDate, currentViewDate, zoomLevel, timelineWidth);
    const endPos = calculateDatePosition(endDate, currentViewDate, zoomLevel, timelineWidth);

    const width = endPos - startPos;

    // Don't render if completely out of view
    if (startPos > timelineWidth || endPos < 0) {
        return null;
    }

    return (
        <div
            className="era-background"
            style={{
                left: `${Math.max(0, startPos)}px`,
                width: `${width}px`,
                backgroundColor: `${era.color_code}20`, // 20% opacity
                borderLeft: `3px solid ${era.color_code}`
            }}
            title={era.name}
        >
            <div className="era-label">{era.name}</div>
        </div>
    );
}

export default EraBackground;
