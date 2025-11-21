/**
 * Timeline Component
 * Main timeline visualization with zoom/pan controls
 */

import React, { useState, useRef, useEffect } from 'react';
import { useTimelineStore } from '../../store/timelineStore';
import TimelineControls from './TimelineControls';
import TimelineCanvas from './TimelineCanvas';
import EventBubble from './EventBubble';
import EraBackground from './EraBackground';
import { calculateDatePosition, getDateRangeForZoom } from '../../utils/timelineUtils';

function Timeline({ onEventClick }) {
    const timelineRef = useRef(null);
    const [timelineWidth, setTimelineWidth] = useState(0);
    const [panOffset, setPanOffset] = useState(0);
    const [isDragging, setIsDragging] = useState(false);
    const [dragStart, setDragStart] = useState({ x: 0, offset: 0 });

    const {
        filteredEvents,
        eras,
        zoomLevel,
        currentViewDate,
        setZoomLevel,
        setCurrentViewDate,
        loadEvents
    } = useTimelineStore();

    // Measure timeline width
    useEffect(() => {
        if (timelineRef.current) {
            const resizeObserver = new ResizeObserver(entries => {
                setTimelineWidth(entries[0].contentRect.width);
            });
            resizeObserver.observe(timelineRef.current);
            return () => resizeObserver.disconnect();
        }
    }, []);

    // Load events when view changes
    useEffect(() => {
        const { start, end } = getDateRangeForZoom(currentViewDate, zoomLevel);
        loadEvents(start, end);
    }, [currentViewDate, zoomLevel]);

    const handleZoomIn = () => {
        const levels = ['year', 'month', 'week', 'day'];
        const currentIndex = levels.indexOf(zoomLevel);
        if (currentIndex < levels.length - 1) {
            setZoomLevel(levels[currentIndex + 1]);
        }
    };

    const handleZoomOut = () => {
        const levels = ['year', 'month', 'week', 'day'];
        const currentIndex = levels.indexOf(zoomLevel);
        if (currentIndex > 0) {
            setZoomLevel(levels[currentIndex - 1]);
        }
    };

    const handleToday = () => {
        setCurrentViewDate(new Date());
        setPanOffset(0);
    };

    const handlePrevious = () => {
        const newDate = new Date(currentViewDate);
        switch (zoomLevel) {
            case 'year':
                newDate.setFullYear(newDate.getFullYear() - 1);
                break;
            case 'month':
                newDate.setMonth(newDate.getMonth() - 1);
                break;
            case 'week':
                newDate.setDate(newDate.getDate() - 7);
                break;
            case 'day':
                newDate.setDate(newDate.getDate() - 1);
                break;
        }
        setCurrentViewDate(newDate);
    };

    const handleNext = () => {
        const newDate = new Date(currentViewDate);
        switch (zoomLevel) {
            case 'year':
                newDate.setFullYear(newDate.getFullYear() + 1);
                break;
            case 'month':
                newDate.setMonth(newDate.getMonth() + 1);
                break;
            case 'week':
                newDate.setDate(newDate.getDate() + 7);
                break;
            case 'day':
                newDate.setDate(newDate.getDate() + 1);
                break;
        }
        setCurrentViewDate(newDate);
    };

    // Mouse drag handlers for panning
    const handleMouseDown = (e) => {
        setIsDragging(true);
        setDragStart({ x: e.clientX, offset: panOffset });
    };

    const handleMouseMove = (e) => {
        if (isDragging) {
            const delta = e.clientX - dragStart.x;
            setPanOffset(dragStart.offset + delta);
        }
    };

    const handleMouseUp = () => {
        setIsDragging(false);
    };

    const handleMouseLeave = () => {
        setIsDragging(false);
    };

    return (
        <div className="timeline-container">
            <TimelineControls
                zoomLevel={zoomLevel}
                onZoomIn={handleZoomIn}
                onZoomOut={handleZoomOut}
                onToday={handleToday}
                onPrevious={handlePrevious}
                onNext={handleNext}
                currentDate={currentViewDate}
            />

            <div
                ref={timelineRef}
                className="timeline-canvas-wrapper"
                onMouseDown={handleMouseDown}
                onMouseMove={handleMouseMove}
                onMouseUp={handleMouseUp}
                onMouseLeave={handleMouseLeave}
                style={{ cursor: isDragging ? 'grabbing' : 'grab' }}
            >
                {/* Era backgrounds */}
                <div className="era-backgrounds" style={{ transform: `translateX(${panOffset}px)` }}>
                    {eras.map(era => (
                        <EraBackground
                            key={era.era_id}
                            era={era}
                            timelineWidth={timelineWidth}
                            zoomLevel={zoomLevel}
                            currentViewDate={currentViewDate}
                        />
                    ))}
                </div>

                {/* Timeline axis */}
                <div className="timeline-axis" style={{ transform: `translateX(${panOffset}px)` }}>
                    <div className="axis-line"></div>
                    {/* TODO: Add date markers based on zoom level */}
                </div>

                {/* Event bubbles */}
                <div className="event-bubbles" style={{ transform: `translateX(${panOffset}px)` }}>
                    {filteredEvents.map(event => (
                        <EventBubble
                            key={event.event_id}
                            event={event}
                            timelineWidth={timelineWidth}
                            zoomLevel={zoomLevel}
                            currentViewDate={currentViewDate}
                            onClick={() => onEventClick(event)}
                        />
                    ))}
                </div>
            </div>

            <div className="timeline-info">
                <span>Showing {filteredEvents.length} events</span>
                <span>Zoom: {zoomLevel}</span>
            </div>
        </div>
    );
}

export default Timeline;
