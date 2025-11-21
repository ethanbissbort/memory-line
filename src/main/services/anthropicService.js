/**
 * Anthropic API Service
 * Handles LLM interactions for event extraction and transcription
 */

const Anthropic = require('@anthropic-ai/sdk');
const fs = require('fs');

class AnthropicService {
    constructor() {
        this.client = null;
        this.apiKey = null;
    }

    /**
     * Initialize the Anthropic client with API key
     * @param {string} apiKey - Anthropic API key
     */
    initialize(apiKey) {
        if (!apiKey) {
            throw new Error('API key is required');
        }

        this.apiKey = apiKey;
        this.client = new Anthropic({
            apiKey: apiKey
        });

        console.log('Anthropic service initialized');
    }

    /**
     * Check if the service is initialized
     * @returns {boolean}
     */
    isInitialized() {
        return this.client !== null && this.apiKey !== null;
    }

    /**
     * Transcribe audio file to text
     * Note: Anthropic doesn't natively support audio transcription yet.
     * This is a placeholder that would need to integrate with a speech-to-text service
     * like Whisper API, Google Speech-to-Text, or Azure Speech.
     *
     * For now, this returns a mock transcription to demonstrate the workflow.
     *
     * @param {string} audioFilePath - Path to audio file
     * @returns {Promise<string>} Transcribed text
     */
    async transcribeAudio(audioFilePath) {
        // TODO: Integrate with actual speech-to-text service
        // Options:
        // 1. OpenAI Whisper API
        // 2. Google Cloud Speech-to-Text
        // 3. Azure Speech Service
        // 4. AWS Transcribe

        // For demonstration, return a mock transcription
        console.log(`Transcribing audio file: ${audioFilePath}`);

        // Mock transcription - in real implementation, call STT service
        return `[MOCK TRANSCRIPTION - Phase 3 Demo]
This is a memory from my college years, specifically around 2015 to 2019.
I was attending Stanford University studying Computer Science.
It was an incredible time of learning and growth.
I made lifelong friends like Sarah Chen and Mike Rodriguez.
We spent countless hours in the Gates Computer Science building working on projects together.
One of my favorite memories was when we won the hackathon in spring 2017 with our AI project.
That really solidified my interest in machine learning and AI.
The experience at Stanford shaped who I am today and led me to my current career in tech.`;
    }

    /**
     * Extract structured event data from transcript
     * @param {string} transcript - Transcribed audio text
     * @returns {Promise<Object>} Extracted event data
     */
    async extractEventData(transcript) {
        if (!this.isInitialized()) {
            throw new Error('Anthropic service not initialized. Please set API key.');
        }

        const prompt = this._buildExtractionPrompt(transcript);

        try {
            const response = await this.client.messages.create({
                model: 'claude-sonnet-4-20250514',
                max_tokens: 4000,
                temperature: 0.3,
                messages: [{
                    role: 'user',
                    content: prompt
                }]
            });

            // Extract JSON from response
            const content = response.content[0].text;
            const jsonMatch = content.match(/\{[\s\S]*\}/);

            if (!jsonMatch) {
                throw new Error('Failed to extract JSON from LLM response');
            }

            const extractedData = JSON.parse(jsonMatch[0]);

            // Validate required fields
            if (!extractedData.title || !extractedData.start_date) {
                throw new Error('Extracted data missing required fields (title, start_date)');
            }

            return {
                success: true,
                data: extractedData,
                rawResponse: content
            };
        } catch (error) {
            console.error('Error extracting event data:', error);
            return {
                success: false,
                error: error.message
            };
        }
    }

    /**
     * Build the extraction prompt for Claude
     * @param {string} transcript - Transcribed text
     * @returns {string} Formatted prompt
     */
    _buildExtractionPrompt(transcript) {
        return `You are helping extract structured event information from a personal memory recording transcript.

TRANSCRIPT:
${transcript}

Extract the following information and return ONLY valid JSON (no markdown, no explanation, just the JSON object):

{
  "title": "Brief, descriptive title for the event or era (max 100 chars)",
  "start_date": "When this event/era began in YYYY-MM-DD format",
  "end_date": "When it ended in YYYY-MM-DD format, or null if it's a point event or ongoing",
  "description": "A comprehensive but concise description (2-4 sentences)",
  "category": "Choose ONE from: milestone, work, education, relationship, travel, achievement, challenge, era, other",
  "suggested_era": "If this is part of a larger life phase, suggest an era name (e.g., 'College Years'), or null",
  "suggested_tags": ["Array of 3-5 relevant tags for categorization"],
  "key_people": ["Array of names of important people mentioned"],
  "locations": ["Array of significant places mentioned"],
  "confidence": 0.85
}

IMPORTANT INSTRUCTIONS:
1. Be factual and preserve the user's voice and meaning
2. For dates, extract the most specific date mentioned, but use reasonable approximations if exact dates aren't given
3. If the transcript describes an extended period (like college years), treat it as an "era" category with start and end dates
4. If information is unclear or not mentioned, use null for optional fields
5. For confidence, assess how clear and complete the information was (0-1 scale)
6. Return ONLY the JSON object, nothing else

JSON:`;
    }

    /**
     * Process a single audio file from queue to pending event
     * @param {string} audioFilePath - Path to audio file
     * @param {string} queueId - Queue item ID
     * @returns {Promise<Object>} Processing result
     */
    async processAudioFile(audioFilePath, queueId) {
        try {
            console.log(`Processing audio file: ${audioFilePath} (queue: ${queueId})`);

            // Step 1: Transcribe audio
            const transcript = await this.transcribeAudio(audioFilePath);

            if (!transcript) {
                throw new Error('Transcription failed or returned empty');
            }

            // Step 2: Extract event data
            const extractionResult = await this.extractEventData(transcript);

            if (!extractionResult.success) {
                throw new Error('Event extraction failed: ' + extractionResult.error);
            }

            return {
                success: true,
                transcript: transcript,
                extractedData: extractionResult.data,
                queueId: queueId
            };
        } catch (error) {
            console.error('Error processing audio file:', error);
            return {
                success: false,
                error: error.message,
                queueId: queueId
            };
        }
    }

    /**
     * Analyze timeline for cross-references (RAG Phase 5)
     * Placeholder for future implementation
     */
    async analyzeTimeline(events) {
        // TODO: Implement RAG-based cross-referencing in Phase 5
        throw new Error('Timeline analysis not yet implemented (Phase 5)');
    }

    /**
     * Generate embeddings for events (RAG Phase 5)
     * Placeholder for future implementation
     */
    async generateEmbeddings(text) {
        // TODO: Implement embedding generation in Phase 5
        // Could use Voyage AI or other embedding services
        throw new Error('Embedding generation not yet implemented (Phase 5)');
    }
}

// Export singleton instance
const anthropicService = new AnthropicService();
module.exports = anthropicService;
