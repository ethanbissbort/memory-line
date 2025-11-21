/**
 * Error Boundary Component
 * Catches JavaScript errors anywhere in the component tree and displays fallback UI
 */

import React from 'react';

class ErrorBoundary extends React.Component {
    constructor(props) {
        super(props);
        this.state = {
            hasError: false,
            error: null,
            errorInfo: null,
            errorCount: 0
        };
    }

    static getDerivedStateFromError(error) {
        // Update state so the next render will show the fallback UI
        return { hasError: true };
    }

    componentDidCatch(error, errorInfo) {
        // Log error to console
        console.error('Error Boundary caught an error:', error, errorInfo);

        // Update state with error details
        this.setState(prevState => ({
            error: error,
            errorInfo: errorInfo,
            errorCount: prevState.errorCount + 1
        }));

        // Log to main process for persistence
        if (window.electronAPI && window.electronAPI.logError) {
            window.electronAPI.logError({
                error: error.toString(),
                stack: error.stack,
                componentStack: errorInfo.componentStack,
                timestamp: new Date().toISOString()
            });
        }
    }

    handleReset = () => {
        this.setState({
            hasError: false,
            error: null,
            errorInfo: null
        });

        // Optionally reload the page if errors persist
        if (this.state.errorCount > 3) {
            window.location.reload();
        }
    };

    handleReload = () => {
        window.location.reload();
    };

    render() {
        if (this.state.hasError) {
            return (
                <div className="error-boundary">
                    <div className="error-boundary-content">
                        <h1>⚠️ Something went wrong</h1>
                        <p className="error-message">
                            The application encountered an unexpected error.
                        </p>

                        {this.state.error && (
                            <details className="error-details">
                                <summary>Error Details</summary>
                                <pre className="error-stack">
                                    <strong>Error:</strong> {this.state.error.toString()}
                                    {'\n\n'}
                                    <strong>Stack:</strong>
                                    {'\n'}
                                    {this.state.error.stack}
                                    {'\n\n'}
                                    {this.state.errorInfo && (
                                        <>
                                            <strong>Component Stack:</strong>
                                            {'\n'}
                                            {this.state.errorInfo.componentStack}
                                        </>
                                    )}
                                </pre>
                            </details>
                        )}

                        <div className="error-actions">
                            <button
                                className="button primary"
                                onClick={this.handleReset}
                            >
                                Try Again
                            </button>
                            <button
                                className="button secondary"
                                onClick={this.handleReload}
                            >
                                Reload Application
                            </button>
                        </div>

                        {this.state.errorCount > 1 && (
                            <p className="error-warning">
                                This error has occurred {this.state.errorCount} times.
                                {this.state.errorCount > 3 && ' The application will reload automatically on next attempt.'}
                            </p>
                        )}
                    </div>
                </div>
            );
        }

        return this.props.children;
    }
}

export default ErrorBoundary;
