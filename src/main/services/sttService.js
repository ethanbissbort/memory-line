/**
 * Speech-to-Text Service
 * Abstraction layer for multiple STT engines (local and remote)
 */

const fs = require('fs');
const path = require('path');

/**
 * STT Engine Options
 *
 * LOCAL ENGINES (Free, Run on User's Machine):
 * 1. Whisper.cpp - Fast C++ implementation of OpenAI Whisper
 *    - Pros: Free, fast, accurate, offline, multilingual
 *    - Cons: Requires native compilation, models need to be downloaded
 *    - Installation: npm install whisper-node OR use whisper.cpp binary
 *    - Models: tiny (39MB), base (74MB), small (244MB), medium (769MB)
 *
 * 2. Vosk - Lightweight speech recognition
 *    - Pros: Free, offline, fast, low resource usage, multilingual
 *    - Cons: Less accurate than Whisper, models need download
 *    - Installation: npm install vosk
 *    - Models: Small models (50MB), larger models (1.8GB)
 *
 * 3. Coqui STT - Fork of Mozilla DeepSpeech
 *    - Pros: Free, offline, open source
 *    - Cons: Less actively maintained, accuracy varies
 *    - Installation: pip install coqui-stt (Python binding needed)
 *
 * REMOTE ENGINES (Paid/API-based):
 * 4. OpenAI Whisper API - Cloud version of Whisper
 *    - Pros: Most accurate, no local resources, no setup
 *    - Cons: Costs $0.006/minute, requires internet, API key
 *    - Installation: npm install openai
 *
 * 5. Google Cloud Speech-to-Text
 *    - Pros: Very accurate, 120+ languages, punctuation, real-time
 *    - Cons: Costs $0.006-0.024/15 seconds, API key, Google account
 *    - Installation: npm install @google-cloud/speech
 *
 * 6. Azure Speech Service
 *    - Pros: Accurate, 100+ languages, speaker recognition
 *    - Cons: Costs ~$1/hour, requires Azure account, API key
 *    - Installation: npm install microsoft-cognitiveservices-speech-sdk
 *
 * 7. AWS Transcribe
 *    - Pros: Accurate, custom vocabulary, speaker identification
 *    - Cons: Costs $0.024/minute, requires AWS account
 *    - Installation: npm install aws-sdk
 *
 * 8. AssemblyAI
 *    - Pros: Good accuracy, easy API, auto-punctuation
 *    - Cons: Costs $0.00025/second ($0.015/minute), API key
 *    - Installation: npm install assemblyai
 *
 * 9. Deepgram
 *    - Pros: Fast, accurate, affordable ($0.0125/minute)
 *    - Cons: API key required
 *    - Installation: npm install @deepgram/sdk
 *
 * RECOMMENDED FOR THIS APP:
 * - Best Free: Whisper.cpp (most accurate, multilingual)
 * - Easiest Setup: OpenAI Whisper API (no local install)
 * - Budget Option: Vosk (lightweight, good enough accuracy)
 * - Enterprise: Google Cloud (best accuracy, features)
 */

class STTService {
    constructor() {
        this.engine = 'mock'; // mock, whisper-local, whisper-api, vosk, google, azure, aws, assemblyai, deepgram
        this.config = {};
    }

    /**
     * Initialize STT service with engine and config
     * @param {string} engine - Engine name
     * @param {Object} config - Engine-specific configuration
     */
    async initialize(engine, config = {}) {
        this.engine = engine;
        this.config = config;

        switch (engine) {
            case 'whisper-local':
                await this._initWhisperLocal();
                break;
            case 'whisper-api':
                await this._initWhisperAPI();
                break;
            case 'vosk':
                await this._initVosk();
                break;
            case 'google':
                await this._initGoogleCloud();
                break;
            case 'azure':
                await this._initAzure();
                break;
            case 'aws':
                await this._initAWS();
                break;
            case 'assemblyai':
                await this._initAssemblyAI();
                break;
            case 'deepgram':
                await this._initDeepgram();
                break;
            case 'mock':
                // No initialization needed for mock
                break;
            default:
                throw new Error(`Unsupported STT engine: ${engine}`);
        }

        console.log(`STT Service initialized with engine: ${engine}`);
    }

    /**
     * Transcribe audio file
     * @param {string} audioFilePath - Path to audio file
     * @returns {Promise<Object>} { success: boolean, transcript: string, error?: string }
     */
    async transcribe(audioFilePath) {
        if (!fs.existsSync(audioFilePath)) {
            return {
                success: false,
                error: 'Audio file not found: ' + audioFilePath
            };
        }

        try {
            let transcript;

            switch (this.engine) {
                case 'mock':
                    transcript = await this._transcribeMock(audioFilePath);
                    break;
                case 'whisper-local':
                    transcript = await this._transcribeWhisperLocal(audioFilePath);
                    break;
                case 'whisper-api':
                    transcript = await this._transcribeWhisperAPI(audioFilePath);
                    break;
                case 'vosk':
                    transcript = await this._transcribeVosk(audioFilePath);
                    break;
                case 'google':
                    transcript = await this._transcribeGoogleCloud(audioFilePath);
                    break;
                case 'azure':
                    transcript = await this._transcribeAzure(audioFilePath);
                    break;
                case 'aws':
                    transcript = await this._transcribeAWS(audioFilePath);
                    break;
                case 'assemblyai':
                    transcript = await this._transcribeAssemblyAI(audioFilePath);
                    break;
                case 'deepgram':
                    transcript = await this._transcribeDeepgram(audioFilePath);
                    break;
                default:
                    throw new Error(`Unsupported STT engine: ${this.engine}`);
            }

            return {
                success: true,
                transcript: transcript
            };
        } catch (error) {
            console.error(`STT transcription failed (${this.engine}):`, error);
            return {
                success: false,
                error: error.message
            };
        }
    }

    // ========================================
    // Mock Engine (for demo/testing)
    // ========================================

    async _transcribeMock(audioFilePath) {
        console.log(`Mock transcription of: ${audioFilePath}`);
        return `[MOCK TRANSCRIPTION - STT Demo]
This is a memory from my college years, specifically around 2015 to 2019.
I was attending Stanford University studying Computer Science.
It was an incredible time of learning and growth.
I made lifelong friends like Sarah Chen and Mike Rodriguez.
We spent countless hours in the Gates Computer Science building working on projects together.
One of my favorite memories was when we won the hackathon in spring 2017 with our AI project.
That really solidified my interest in machine learning and AI.
The experience at Stanford shaped who I am today and led me to my current career in tech.`;
    }

    // ========================================
    // Whisper Local (whisper.cpp)
    // ========================================

    async _initWhisperLocal() {
        // Check if whisper-node is installed
        try {
            this.whisperNode = require('whisper-node');
            console.log('Whisper Local initialized');
        } catch (error) {
            throw new Error('whisper-node not installed. Run: npm install whisper-node');
        }
    }

    async _transcribeWhisperLocal(audioFilePath) {
        const model = this.config.model || 'base'; // tiny, base, small, medium, large
        const language = this.config.language || 'en';

        const whisper = this.whisperNode.whisper;
        const result = await whisper(audioFilePath, {
            modelName: model,
            language: language
        });

        return result.map(r => r.speech).join(' ');
    }

    // ========================================
    // OpenAI Whisper API
    // ========================================

    async _initWhisperAPI() {
        const OpenAI = require('openai');
        this.openai = new OpenAI({
            apiKey: this.config.apiKey
        });
        console.log('OpenAI Whisper API initialized');
    }

    async _transcribeWhisperAPI(audioFilePath) {
        const transcription = await this.openai.audio.transcriptions.create({
            file: fs.createReadStream(audioFilePath),
            model: 'whisper-1',
            language: this.config.language || 'en'
        });

        return transcription.text;
    }

    // ========================================
    // Vosk (offline)
    // ========================================

    async _initVosk() {
        try {
            this.vosk = require('vosk');
            const modelPath = this.config.modelPath;
            if (!modelPath || !fs.existsSync(modelPath)) {
                throw new Error('Vosk model path not found. Download from: https://alphacephei.com/vosk/models');
            }
            this.voskModel = new this.vosk.Model(modelPath);
            console.log('Vosk initialized');
        } catch (error) {
            throw new Error('Vosk not installed. Run: npm install vosk');
        }
    }

    async _transcribeVosk(audioFilePath) {
        // Vosk requires WAV format at 16kHz mono
        // Implementation would use vosk recognizer
        const recognizer = new this.vosk.Recognizer({
            model: this.voskModel,
            sampleRate: 16000
        });

        // Process audio file in chunks
        // This is simplified - real implementation would stream audio
        const result = recognizer.acceptWaveform(/* audio buffer */);
        const transcript = JSON.parse(recognizer.finalResult()).text;

        return transcript;
    }

    // ========================================
    // Google Cloud Speech-to-Text
    // ========================================

    async _initGoogleCloud() {
        const speech = require('@google-cloud/speech');
        this.speechClient = new speech.SpeechClient({
            keyFilename: this.config.keyFilePath
        });
        console.log('Google Cloud Speech initialized');
    }

    async _transcribeGoogleCloud(audioFilePath) {
        const audioBytes = fs.readFileSync(audioFilePath).toString('base64');

        const request = {
            audio: {
                content: audioBytes
            },
            config: {
                encoding: 'LINEAR16',
                sampleRateHertz: 16000,
                languageCode: this.config.language || 'en-US',
                enableAutomaticPunctuation: true
            }
        };

        const [response] = await this.speechClient.recognize(request);
        const transcription = response.results
            .map(result => result.alternatives[0].transcript)
            .join('\n');

        return transcription;
    }

    // ========================================
    // Azure Speech Service
    // ========================================

    async _initAzure() {
        const sdk = require('microsoft-cognitiveservices-speech-sdk');
        const speechConfig = sdk.SpeechConfig.fromSubscription(
            this.config.subscriptionKey,
            this.config.region
        );
        this.speechConfig = speechConfig;
        console.log('Azure Speech Service initialized');
    }

    async _transcribeAzure(audioFilePath) {
        const sdk = require('microsoft-cognitiveservices-speech-sdk');
        const audioConfig = sdk.AudioConfig.fromWavFileInput(
            fs.readFileSync(audioFilePath)
        );
        const recognizer = new sdk.SpeechRecognizer(this.speechConfig, audioConfig);

        return new Promise((resolve, reject) => {
            recognizer.recognizeOnceAsync(result => {
                if (result.reason === sdk.ResultReason.RecognizedSpeech) {
                    resolve(result.text);
                } else {
                    reject(new Error('Azure transcription failed'));
                }
                recognizer.close();
            });
        });
    }

    // ========================================
    // AWS Transcribe
    // ========================================

    async _initAWS() {
        const AWS = require('aws-sdk');
        AWS.config.update({
            accessKeyId: this.config.accessKeyId,
            secretAccessKey: this.config.secretAccessKey,
            region: this.config.region || 'us-east-1'
        });
        this.transcribeService = new AWS.TranscribeService();
        console.log('AWS Transcribe initialized');
    }

    async _transcribeAWS(audioFilePath) {
        // AWS Transcribe requires uploading to S3 first
        // This is a simplified version
        throw new Error('AWS Transcribe requires S3 upload - not yet implemented');
    }

    // ========================================
    // AssemblyAI
    // ========================================

    async _initAssemblyAI() {
        const { AssemblyAI } = require('assemblyai');
        this.assemblyai = new AssemblyAI({
            apiKey: this.config.apiKey
        });
        console.log('AssemblyAI initialized');
    }

    async _transcribeAssemblyAI(audioFilePath) {
        const transcript = await this.assemblyai.transcripts.create({
            audio_url: audioFilePath // Or upload file first
        });

        // Poll for completion
        let result = transcript;
        while (result.status !== 'completed' && result.status !== 'error') {
            await new Promise(resolve => setTimeout(resolve, 1000));
            result = await this.assemblyai.transcripts.get(transcript.id);
        }

        if (result.status === 'error') {
            throw new Error('AssemblyAI transcription failed');
        }

        return result.text;
    }

    // ========================================
    // Deepgram
    // ========================================

    async _initDeepgram() {
        const { createClient } = require('@deepgram/sdk');
        this.deepgram = createClient(this.config.apiKey);
        console.log('Deepgram initialized');
    }

    async _transcribeDeepgram(audioFilePath) {
        const audioBuffer = fs.readFileSync(audioFilePath);

        const { result, error } = await this.deepgram.listen.prerecorded.transcribeFile(
            audioBuffer,
            {
                model: 'nova-2',
                language: this.config.language || 'en',
                punctuate: true
            }
        );

        if (error) {
            throw error;
        }

        return result.results.channels[0].alternatives[0].transcript;
    }

    /**
     * Get list of available engines
     * @returns {Array} List of engine info objects
     */
    static getAvailableEngines() {
        return [
            {
                id: 'mock',
                name: 'Mock (Demo Only)',
                type: 'local',
                cost: 'free',
                setup: 'none',
                accuracy: 'n/a',
                description: 'Returns demo transcript for testing',
                recommended: false
            },
            {
                id: 'whisper-local',
                name: 'Whisper.cpp (Local)',
                type: 'local',
                cost: 'free',
                setup: 'npm install whisper-node',
                accuracy: 'excellent',
                description: 'OpenAI Whisper running locally. Best free option.',
                recommended: true,
                models: ['tiny', 'base', 'small', 'medium', 'large'],
                modelSizes: ['39MB', '74MB', '244MB', '769MB', '1.5GB']
            },
            {
                id: 'vosk',
                name: 'Vosk (Local)',
                type: 'local',
                cost: 'free',
                setup: 'npm install vosk + download model',
                accuracy: 'good',
                description: 'Lightweight offline speech recognition',
                recommended: true,
                modelUrl: 'https://alphacephei.com/vosk/models'
            },
            {
                id: 'whisper-api',
                name: 'OpenAI Whisper API',
                type: 'remote',
                cost: '$0.006/minute',
                setup: 'API key from OpenAI',
                accuracy: 'excellent',
                description: 'Cloud-based Whisper. No local resources needed.',
                recommended: true,
                apiUrl: 'https://platform.openai.com/api-keys'
            },
            {
                id: 'google',
                name: 'Google Cloud Speech-to-Text',
                type: 'remote',
                cost: '$0.006-0.024/15sec',
                setup: 'Google Cloud account + API key',
                accuracy: 'excellent',
                description: '120+ languages, punctuation, real-time',
                recommended: false,
                apiUrl: 'https://console.cloud.google.com'
            },
            {
                id: 'azure',
                name: 'Azure Speech Service',
                type: 'remote',
                cost: '~$1/hour',
                setup: 'Azure account + subscription key',
                accuracy: 'excellent',
                description: '100+ languages, speaker recognition',
                recommended: false,
                apiUrl: 'https://azure.microsoft.com/en-us/services/cognitive-services/speech-services/'
            },
            {
                id: 'assemblyai',
                name: 'AssemblyAI',
                type: 'remote',
                cost: '$0.00025/second',
                setup: 'API key from AssemblyAI',
                accuracy: 'very good',
                description: 'Easy API, auto-punctuation',
                recommended: false,
                apiUrl: 'https://www.assemblyai.com/'
            },
            {
                id: 'deepgram',
                name: 'Deepgram',
                type: 'remote',
                cost: '$0.0125/minute',
                setup: 'API key from Deepgram',
                accuracy: 'very good',
                description: 'Fast and affordable',
                recommended: false,
                apiUrl: 'https://deepgram.com/'
            }
        ];
    }
}

// Export singleton instance
const sttService = new STTService();
module.exports = sttService;
