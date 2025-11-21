/**
 * Event Details Modal Component
 * Displays detailed information about an event
 */

import React, { useState } from 'react';
import { format, parseISO } from 'date-fns';
import { useTimelineStore } from '../../store/timelineStore';

function EventDetailsModal({ event, onClose }) {
    const [isEditing, setIsEditing] = useState(false);
    const [editedEvent, setEditedEvent] = useState({ ...event });
    const { updateEvent, deleteEvent } = useTimelineStore();

    const handleSave = async () => {
        const result = await updateEvent(event.event_id, editedEvent);
        if (result.success) {
            setIsEditing(false);
            onClose();
        } else {
            alert('Failed to update event: ' + result.error);
        }
    };

    const handleDelete = async () => {
        if (confirm('Are you sure you want to delete this event?')) {
            const result = await deleteEvent(event.event_id);
            if (result.success) {
                onClose();
            } else {
                alert('Failed to delete event: ' + result.error);
            }
        }
    };

    const handleChange = (field, value) => {
        setEditedEvent(prev => ({ ...prev, [field]: value }));
    };

    return (
        <div className="modal-overlay" onClick={onClose}>
            <div className="modal-content" onClick={e => e.stopPropagation()}>
                <div className="modal-header">
                    <h2>{isEditing ? 'Edit Event' : 'Event Details'}</h2>
                    <button className="close-button" onClick={onClose}>×</button>
                </div>

                <div className="modal-body">
                    {isEditing ? (
                        <>
                            <div className="form-group">
                                <label>Title</label>
                                <input
                                    type="text"
                                    value={editedEvent.title}
                                    onChange={e => handleChange('title', e.target.value)}
                                />
                            </div>

                            <div className="form-group">
                                <label>Start Date</label>
                                <input
                                    type="date"
                                    value={editedEvent.start_date}
                                    onChange={e => handleChange('start_date', e.target.value)}
                                />
                            </div>

                            <div className="form-group">
                                <label>End Date (optional)</label>
                                <input
                                    type="date"
                                    value={editedEvent.end_date || ''}
                                    onChange={e => handleChange('end_date', e.target.value)}
                                />
                            </div>

                            <div className="form-group">
                                <label>Description</label>
                                <textarea
                                    rows="6"
                                    value={editedEvent.description || ''}
                                    onChange={e => handleChange('description', e.target.value)}
                                />
                            </div>

                            <div className="form-group">
                                <label>Category</label>
                                <select
                                    value={editedEvent.category}
                                    onChange={e => handleChange('category', e.target.value)}
                                >
                                    <option value="milestone">Milestone</option>
                                    <option value="work">Work</option>
                                    <option value="education">Education</option>
                                    <option value="relationship">Relationship</option>
                                    <option value="travel">Travel</option>
                                    <option value="achievement">Achievement</option>
                                    <option value="challenge">Challenge</option>
                                    <option value="era">Era</option>
                                    <option value="other">Other</option>
                                </select>
                            </div>
                        </>
                    ) : (
                        <>
                            <div className="detail-section">
                                <h3>{event.title}</h3>
                                <p className="event-date-range">
                                    {format(parseISO(event.start_date), 'MMMM d, yyyy')}
                                    {event.end_date && ` - ${format(parseISO(event.end_date), 'MMMM d, yyyy')}`}
                                </p>
                            </div>

                            {event.category && (
                                <div className="detail-section">
                                    <strong>Category:</strong> {event.category}
                                </div>
                            )}

                            {event.description && (
                                <div className="detail-section">
                                    <strong>Description:</strong>
                                    <p>{event.description}</p>
                                </div>
                            )}

                            {event.era_name && (
                                <div className="detail-section">
                                    <strong>Era:</strong> {event.era_name}
                                </div>
                            )}

                            {event.tags && event.tags.length > 0 && (
                                <div className="detail-section">
                                    <strong>Tags:</strong>
                                    <div className="tag-list">
                                        {event.tags.map((tag, idx) => (
                                            <span key={idx} className="tag">{tag}</span>
                                        ))}
                                    </div>
                                </div>
                            )}

                            {event.people && event.people.length > 0 && (
                                <div className="detail-section">
                                    <strong>People:</strong> {event.people.join(', ')}
                                </div>
                            )}

                            {event.locations && event.locations.length > 0 && (
                                <div className="detail-section">
                                    <strong>Locations:</strong> {event.locations.join(', ')}
                                </div>
                            )}

                            {event.raw_transcript && (
                                <div className="detail-section">
                                    <strong>Transcript:</strong>
                                    <p className="transcript">{event.raw_transcript}</p>
                                </div>
                            )}

                            {event.audio_file_path && (
                                <div className="detail-section">
                                    <strong>Audio Recording:</strong>
                                    <audio controls src={event.audio_file_path} />
                                </div>
                            )}
                        </>
                    )}
                </div>

                <div className="modal-footer">
                    {isEditing ? (
                        <>
                            <button className="button secondary" onClick={() => setIsEditing(false)}>
                                Cancel
                            </button>
                            <button className="button primary" onClick={handleSave}>
                                Save Changes
                            </button>
                        </>
                    ) : (
                        <>
                            <button className="button danger" onClick={handleDelete}>
                                Delete
                            </button>
                            <button className="button secondary" onClick={() => setIsEditing(true)}>
                                Edit
                            </button>
                            <button className="button primary" onClick={onClose}>
                                Close
                            </button>
                        </>
                    )}
                </div>
            </div>
        </div>
    );
}

export default EventDetailsModal;
