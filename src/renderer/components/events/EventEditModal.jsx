/**
 * Event Edit Modal Component
 * Allows editing extracted event data before approval
 */

import React, { useState, useEffect } from 'react';

function EventEditModal({ isOpen, onClose, eventData, onSave }) {
    const [formData, setFormData] = useState({
        title: '',
        start_date: '',
        end_date: '',
        description: '',
        category: 'milestone',
        suggested_era: '',
        suggested_tags: [],
        key_people: [],
        locations: [],
        confidence: 0
    });

    const [newTag, setNewTag] = useState('');
    const [newPerson, setNewPerson] = useState('');
    const [newLocation, setNewLocation] = useState('');

    useEffect(() => {
        if (eventData) {
            setFormData({
                title: eventData.title || '',
                start_date: eventData.start_date || '',
                end_date: eventData.end_date || '',
                description: eventData.description || '',
                category: eventData.category || 'milestone',
                suggested_era: eventData.suggested_era || '',
                suggested_tags: eventData.suggested_tags || [],
                key_people: eventData.key_people || [],
                locations: eventData.locations || [],
                confidence: eventData.confidence || 0
            });
        }
    }, [eventData]);

    if (!isOpen) return null;

    const handleChange = (field, value) => {
        setFormData({
            ...formData,
            [field]: value
        });
    };

    const handleAddTag = () => {
        if (newTag.trim() && !formData.suggested_tags.includes(newTag.trim())) {
            setFormData({
                ...formData,
                suggested_tags: [...formData.suggested_tags, newTag.trim()]
            });
            setNewTag('');
        }
    };

    const handleRemoveTag = (tag) => {
        setFormData({
            ...formData,
            suggested_tags: formData.suggested_tags.filter(t => t !== tag)
        });
    };

    const handleAddPerson = () => {
        if (newPerson.trim() && !formData.key_people.includes(newPerson.trim())) {
            setFormData({
                ...formData,
                key_people: [...formData.key_people, newPerson.trim()]
            });
            setNewPerson('');
        }
    };

    const handleRemovePerson = (person) => {
        setFormData({
            ...formData,
            key_people: formData.key_people.filter(p => p !== person)
        });
    };

    const handleAddLocation = () => {
        if (newLocation.trim() && !formData.locations.includes(newLocation.trim())) {
            setFormData({
                ...formData,
                locations: [...formData.locations, newLocation.trim()]
            });
            setNewLocation('');
        }
    };

    const handleRemoveLocation = (location) => {
        setFormData({
            ...formData,
            locations: formData.locations.filter(l => l !== location)
        });
    };

    const handleSave = () => {
        onSave(formData);
        onClose();
    };

    const categories = [
        'milestone',
        'work',
        'education',
        'relationship',
        'travel',
        'achievement',
        'challenge',
        'era',
        'health',
        'hobby',
        'family',
        'social',
        'financial',
        'personal',
        'other'
    ];

    return (
        <div className="modal-overlay" onClick={onClose}>
            <div className="modal-content event-edit-modal" onClick={(e) => e.stopPropagation()}>
                <div className="modal-header">
                    <h2>Edit Event</h2>
                    <button className="close-button" onClick={onClose}>&times;</button>
                </div>

                <div className="modal-body">
                    <div className="form-section">
                        <div className="form-group">
                            <label>Title *</label>
                            <input
                                type="text"
                                value={formData.title}
                                onChange={(e) => handleChange('title', e.target.value)}
                                placeholder="Event title"
                            />
                        </div>

                        <div className="form-row">
                            <div className="form-group">
                                <label>Start Date *</label>
                                <input
                                    type="date"
                                    value={formData.start_date}
                                    onChange={(e) => handleChange('start_date', e.target.value)}
                                />
                            </div>

                            <div className="form-group">
                                <label>End Date</label>
                                <input
                                    type="date"
                                    value={formData.end_date || ''}
                                    onChange={(e) => handleChange('end_date', e.target.value)}
                                />
                            </div>
                        </div>

                        <div className="form-group">
                            <label>Category</label>
                            <select
                                value={formData.category}
                                onChange={(e) => handleChange('category', e.target.value)}
                            >
                                {categories.map(cat => (
                                    <option key={cat} value={cat}>
                                        {cat.charAt(0).toUpperCase() + cat.slice(1)}
                                    </option>
                                ))}
                            </select>
                        </div>

                        <div className="form-group">
                            <label>Description</label>
                            <textarea
                                value={formData.description}
                                onChange={(e) => handleChange('description', e.target.value)}
                                rows="4"
                                placeholder="Describe the event..."
                            />
                        </div>

                        <div className="form-group">
                            <label>Suggested Era</label>
                            <input
                                type="text"
                                value={formData.suggested_era || ''}
                                onChange={(e) => handleChange('suggested_era', e.target.value)}
                                placeholder="e.g., College Years, Early Career"
                            />
                        </div>

                        <div className="form-group">
                            <label>Tags</label>
                            <div className="list-input">
                                <input
                                    type="text"
                                    value={newTag}
                                    onChange={(e) => setNewTag(e.target.value)}
                                    onKeyPress={(e) => e.key === 'Enter' && (e.preventDefault(), handleAddTag())}
                                    placeholder="Add a tag..."
                                />
                                <button
                                    type="button"
                                    className="button secondary small"
                                    onClick={handleAddTag}
                                >
                                    Add
                                </button>
                            </div>
                            <div className="tag-list">
                                {formData.suggested_tags.map((tag, idx) => (
                                    <span key={idx} className="tag">
                                        {tag}
                                        <button
                                            className="tag-remove"
                                            onClick={() => handleRemoveTag(tag)}
                                        >
                                            &times;
                                        </button>
                                    </span>
                                ))}
                            </div>
                        </div>

                        <div className="form-group">
                            <label>People</label>
                            <div className="list-input">
                                <input
                                    type="text"
                                    value={newPerson}
                                    onChange={(e) => setNewPerson(e.target.value)}
                                    onKeyPress={(e) => e.key === 'Enter' && (e.preventDefault(), handleAddPerson())}
                                    placeholder="Add a person..."
                                />
                                <button
                                    type="button"
                                    className="button secondary small"
                                    onClick={handleAddPerson}
                                >
                                    Add
                                </button>
                            </div>
                            <div className="tag-list">
                                {formData.key_people.map((person, idx) => (
                                    <span key={idx} className="tag">
                                        {person}
                                        <button
                                            className="tag-remove"
                                            onClick={() => handleRemovePerson(person)}
                                        >
                                            &times;
                                        </button>
                                    </span>
                                ))}
                            </div>
                        </div>

                        <div className="form-group">
                            <label>Locations</label>
                            <div className="list-input">
                                <input
                                    type="text"
                                    value={newLocation}
                                    onChange={(e) => setNewLocation(e.target.value)}
                                    onKeyPress={(e) => e.key === 'Enter' && (e.preventDefault(), handleAddLocation())}
                                    placeholder="Add a location..."
                                />
                                <button
                                    type="button"
                                    className="button secondary small"
                                    onClick={handleAddLocation}
                                >
                                    Add
                                </button>
                            </div>
                            <div className="tag-list">
                                {formData.locations.map((location, idx) => (
                                    <span key={idx} className="tag">
                                        {location}
                                        <button
                                            className="tag-remove"
                                            onClick={() => handleRemoveLocation(location)}
                                        >
                                            &times;
                                        </button>
                                    </span>
                                ))}
                            </div>
                        </div>

                        <div className="form-group">
                            <label>Confidence Score</label>
                            <div className="confidence-display">
                                {Math.round(formData.confidence * 100)}%
                            </div>
                        </div>
                    </div>
                </div>

                <div className="modal-footer">
                    <button className="button secondary" onClick={onClose}>
                        Cancel
                    </button>
                    <button
                        className="button primary"
                        onClick={handleSave}
                        disabled={!formData.title || !formData.start_date}
                    >
                        Save Changes
                    </button>
                </div>
            </div>
        </div>
    );
}

export default EventEditModal;
