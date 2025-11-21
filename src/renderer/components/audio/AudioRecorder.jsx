/**
 * Audio Recorder Component
 * Interface for recording audio memories
 */

import React, { useState, useRef } from 'react';

function AudioRecorder() {
    const [isRecording, setIsRecording] = useState(false);
    const [isPaused, setIsPaused] = useState(false);
    const [recordingTime, setRecordingTime] = useState(0);
    const [audioURL, setAudioURL] = useState(null);
    const [audioLevel, setAudioLevel] = useState(0);

    const mediaRecorderRef = useRef(null);
    const audioChunksRef = useRef([]);
    const timerRef = useRef(null);

    const startRecording = async () => {
        try {
            const stream = await navigator.mediaDevices.getUserMedia({ audio: true });

            mediaRecorderRef.current = new MediaRecorder(stream);
            audioChunksRef.current = [];

            mediaRecorderRef.current.ondataavailable = (event) => {
                audioChunksRef.current.push(event.data);
            };

            mediaRecorderRef.current.onstop = () => {
                const audioBlob = new Blob(audioChunksRef.current, { type: 'audio/wav' });
                const url = URL.createObjectURL(audioBlob);
                setAudioURL(url);
                stream.getTracks().forEach(track => track.stop());
            };

            mediaRecorderRef.current.start();
            setIsRecording(true);
            setRecordingTime(0);

            // Start timer
            timerRef.current = setInterval(() => {
                setRecordingTime(prev => prev + 1);
            }, 1000);

            // TODO: Add audio level monitoring
        } catch (error) {
            console.error('Error starting recording:', error);
            alert('Failed to access microphone. Please check permissions.');
        }
    };

    const pauseRecording = () => {
        if (mediaRecorderRef.current && isRecording) {
            mediaRecorderRef.current.pause();
            setIsPaused(true);
            clearInterval(timerRef.current);
        }
    };

    const resumeRecording = () => {
        if (mediaRecorderRef.current && isPaused) {
            mediaRecorderRef.current.resume();
            setIsPaused(false);

            // Resume timer
            timerRef.current = setInterval(() => {
                setRecordingTime(prev => prev + 1);
            }, 1000);
        }
    };

    const stopRecording = () => {
        if (mediaRecorderRef.current) {
            mediaRecorderRef.current.stop();
            setIsRecording(false);
            setIsPaused(false);
            clearInterval(timerRef.current);
        }
    };

    const cancelRecording = () => {
        if (mediaRecorderRef.current) {
            mediaRecorderRef.current.stop();
            setIsRecording(false);
            setIsPaused(false);
            setRecordingTime(0);
            setAudioURL(null);
            clearInterval(timerRef.current);
        }
    };

    const submitToQueue = async () => {
        if (!audioURL) return;

        // TODO: Save audio file and add to processing queue
        alert('Audio submitted to queue! (Implementation pending)');
        setAudioURL(null);
        setRecordingTime(0);
    };

    const formatTime = (seconds) => {
        const mins = Math.floor(seconds / 60);
        const secs = seconds % 60;
        return `${mins}:${secs.toString().padStart(2, '0')}`;
    };

    return (
        <div className="audio-recorder">
            <div className="recorder-header">
                <h2>Record Audio Memory</h2>
                <p>Record yourself describing a memory or life event</p>
            </div>

            <div className="recorder-main">
                {!isRecording && !audioURL && (
                    <div className="recorder-idle">
                        <button
                            className="record-button large"
                            onClick={startRecording}
                        >
                            <span className="record-icon">⏺</span>
                            Start Recording
                        </button>
                    </div>
                )}

                {isRecording && (
                    <div className="recorder-active">
                        <div className="recording-indicator">
                            <span className="pulse-dot"></span>
                            Recording...
                        </div>

                        <div className="recording-time">
                            {formatTime(recordingTime)}
                        </div>

                        <div className="audio-level-meter">
                            <div
                                className="audio-level-bar"
                                style={{ width: `${audioLevel}%` }}
                            />
                        </div>

                        <div className="recording-controls">
                            {isPaused ? (
                                <button className="control-button" onClick={resumeRecording}>
                                    Resume
                                </button>
                            ) : (
                                <button className="control-button" onClick={pauseRecording}>
                                    Pause
                                </button>
                            )}

                            <button className="control-button danger" onClick={cancelRecording}>
                                Cancel
                            </button>

                            <button className="control-button primary" onClick={stopRecording}>
                                Stop
                            </button>
                        </div>
                    </div>
                )}

                {audioURL && !isRecording && (
                    <div className="recorder-preview">
                        <div className="preview-header">
                            <h3>Recording Complete</h3>
                            <p>Duration: {formatTime(recordingTime)}</p>
                        </div>

                        <audio controls src={audioURL} className="audio-player" />

                        <div className="preview-actions">
                            <button
                                className="button secondary"
                                onClick={() => {
                                    setAudioURL(null);
                                    setRecordingTime(0);
                                }}
                            >
                                Delete & Re-record
                            </button>

                            <button
                                className="button primary"
                                onClick={submitToQueue}
                            >
                                Submit to Queue
                            </button>
                        </div>
                    </div>
                )}
            </div>

            <div className="recorder-tips">
                <h4>Recording Tips:</h4>
                <ul>
                    <li>Speak clearly and at a moderate pace</li>
                    <li>Include dates, locations, and names of people involved</li>
                    <li>Describe the significance of the event or memory</li>
                    <li>Maximum recording length: 30 minutes</li>
                </ul>
            </div>
        </div>
    );
}

export default AudioRecorder;
