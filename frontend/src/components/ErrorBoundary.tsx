import { Component, type ErrorInfo, type ReactNode } from 'react'

interface ErrorBoundaryProps {
  children: ReactNode
}

interface ErrorBoundaryState {
  error: Error | null
}

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { error: null }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { error }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('UI crashed:', error, info.componentStack)
  }

  render() {
    if (this.state.error) {
      return (
        <div
          role="alert"
          className="mx-auto max-w-xl mt-20 rounded-2xl border border-border bg-card p-6 space-y-3"
        >
          <h1 className="text-lg font-semibold">Something went wrong.</h1>
          <p className="text-sm text-muted">
            The chat UI hit an unexpected error. Reload the page to recover.
          </p>
          <pre className="text-xs bg-surface border border-border rounded-md p-3 overflow-x-auto">
            {this.state.error.message}
          </pre>
          <button
            type="button"
            onClick={() => window.location.reload()}
            className="text-sm rounded-md border border-border bg-surface/70 px-3 py-1.5 hover:bg-accent/60 transition"
          >
            Reload
          </button>
        </div>
      )
    }
    return this.props.children
  }
}
