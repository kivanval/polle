import { Component, Fragment, type ErrorInfo, type ReactNode } from 'react'
import { isDomDesyncError } from './domDesync'

type Props = {
  children: ReactNode
  /** What the fallback calls the broken part, e.g. "this strategy". */
  label: string
  /** After a failure, a new value clears it and remounts the children, so new data gets a fresh DOM. */
  resetKey?: unknown
  /** Adds a "Close" button to the fallback, for overlays that would otherwise trap the user. */
  onDismiss?: () => void
  /** Classes for the fallback, so it takes the place of the broken part (e.g. a card in the grid). */
  className?: string
}
type State = { error: Error | null; resetKey: unknown; attempt: number }

/**
 * Keeps a render failure inside one part of the plan view instead of blanking the page. "Show again" remounts the
 * children from scratch, which also repairs a DOM that a browser translator rewrote under React (NotFoundError on
 * insertBefore/removeChild).
 */
export class ErrorBoundary extends Component<Props, State> {
  state: State = { error: null, resetKey: this.props.resetKey, attempt: 0 }

  static getDerivedStateFromError(error: unknown): Partial<State> {
    return { error: error instanceof Error ? error : new Error(String(error)) }
  }

  /** New data clears a failure and remounts; while nothing has failed the children keep their state. */
  static getDerivedStateFromProps(props: Props, state: State): Partial<State> | null {
    if (Object.is(props.resetKey, state.resetKey)) return null
    return state.error ? { error: null, resetKey: props.resetKey, attempt: state.attempt + 1 } : { resetKey: props.resetKey }
  }

  componentDidCatch(error: unknown, info: ErrorInfo) {
    console.error(`Render failed in ${this.props.label}`, error, info.componentStack)
  }

  private retry = () => this.setState(state => ({ error: null, attempt: state.attempt + 1 }))

  render() {
    const { error, attempt } = this.state
    if (!error) return <Fragment key={attempt}>{this.props.children}</Fragment>
    return <div className={`render-fallback ${this.props.className ?? ''}`} role="alert">
      <p><span>{isDomDesyncError(error)
        ? `The page changed under ${this.props.label}, for example by a browser translator.`
        : `Something went wrong showing ${this.props.label}.`}</span></p>
      <div className="render-fallback-actions">
        <button type="button" className="button button-quiet" onClick={this.retry}><span>Show again</span></button>
        {this.props.onDismiss && <button type="button" className="button button-quiet" onClick={this.props.onDismiss}><span>Close</span></button>}
      </div>
    </div>
  }
}
