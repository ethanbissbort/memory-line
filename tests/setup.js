/**
 * Jest Test Setup
 * Runs before all tests
 */

// Mock Electron APIs
global.mockElectronAPI = {
    events: {
        getRange: jest.fn(),
        getById: jest.fn(),
        create: jest.fn(),
        update: jest.fn(),
        delete: jest.fn()
    },
    settings: {
        getAll: jest.fn(),
        update: jest.fn()
    },
    llm: {
        setApiKey: jest.fn(),
        hasApiKey: jest.fn()
    }
};

// Mock console methods to reduce noise
global.console = {
    ...console,
    log: jest.fn(),
    debug: jest.fn(),
    info: jest.fn(),
    warn: jest.fn(),
    error: jest.fn()
};

// Set test environment variables
process.env.NODE_ENV = 'test';
