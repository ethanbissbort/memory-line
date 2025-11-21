/**
 * Timeline Controls Component
 * Zoom, navigation, and view controls for the timeline
 */

import React from 'react';
import { format } from 'date-fns';

function TimelineControls({
    zoomLevel,
    onZoomIn,
    onZoomOut,
    onToday,
    onPrevious,
    onNext,
    currentDate
}) {
    return (
        <div className="timeline-controls">
            <div className="controls-group">
                <button
                    className="control-button"
                    onClick={onPrevious}
                    title="Previous"
                >
                    &lt;
                </button>
                <button
                    className="control-button"
                    onClick={onToday}
                    title="Go to Today"
                >
                    Today
                </button>
                <button
                    className="control-button"
                    onClick={onNext}
                    title="Next"
                >
                    &gt;
                </button>
            </div>

            <div className="controls-group">
                <span className="current-date">
                    {format(currentDate, 'MMMM yyyy')}
                </span>
            </div>

            <div className="controls-group">
                <button
                    className="control-button"
                    onClick={onZoomOut}
                    disabled={zoomLevel === 'year'}
                    title="Zoom Out"
                >
                    -
                </button>
                <span className="zoom-level">{zoomLevel}</span>
                <button
                    className="control-button"
                    onClick={onZoomIn}
                    disabled={zoomLevel === 'day'}
                    title="Zoom In"
                >
                    +
                </button>
            </div>
        </div>
    );
}

export default TimelineControls;
