import { useEffect, useRef, useState, type CSSProperties, type FormEvent, type KeyboardEvent } from 'react'
import { ArrowUp, Bot, Check, Copy, RotateCcw, X } from 'lucide-react'
import { useLocation } from 'react-router'
import { apiFetch, ApiError } from '@/shared/api/client'
import { MessageText } from '@/features/assistant/MessageText'
import { useAssistantPageSelection } from '@/features/assistant/useAssistantPageSelection'
import { maxMessageChars, receive, requestBody, type ChatMessage, type ChatResponse, type ConversationContext, type PageContext } from '@/features/assistant/chat'
import './assistant.css'

const examples = ['Which plan is best for my field?', 'Analyze my field for 5 years.', 'What farms do I have?', 'What can you do?']
const maxMessages = 100
// The API stops within its 100-second budget, so a browser timeout means no response arrived.
const requestTimeoutMs = 110_000

function errorText(error: unknown) {
  if (error instanceof ApiError) {
    if (error.status === 504) return 'The assistant took too long. Try again.'
    if (error.status === 502) return 'The model could not finish this answer. Try a shorter or more specific question.'
    // The API explains the cause: local Ollama not running, a missing model, or a used-up hosted quota.
    if (error.status === 503) return error.message
    if (error.status === 429) return 'Too many requests. Wait a minute and try again.'
    if (error.status === 413) return 'This message is too large to send. Shorten it and try again.'
    if (error.status === 401) return 'Your session has ended. Sign in again.'
    if (error.status === 400) return 'The request could not be processed. Please rephrase it.'
  }
  if (error instanceof DOMException && error.name === 'TimeoutError') return 'No response arrived in time. The connection may have been lost. Try again.'
  if (error instanceof TypeError) return 'Could not reach Polle. Check your connection and try again.'
  return 'Could not get a response. Try again.'
}

// Resending an identical rejected payload cannot succeed.
const canRetry = (error: unknown) => !(error instanceof ApiError && (error.status === 400 || error.status === 413))

export function AssistantChat({ closeForNavigation = false, footerElement }: {
  closeForNavigation?: boolean
  footerElement?: HTMLElement | null
}) {
  const [open, setOpen] = useState(false)
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [draft, setDraft] = useState('')
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [failed, setFailed] = useState<{ message: string; history: ChatMessage[]; context: ConversationContext | null; page: PageContext } | null>(null)
  const [footerLift, setFooterLift] = useState(0)
  const [copiedId, setCopiedId] = useState<number | null>(null)
  const conversation = useRef<ConversationContext | null>(null)
  const nextId = useRef(1)
  const transcript = useRef<HTMLDivElement>(null)
  const input = useRef<HTMLTextAreaElement>(null)
  const toggle = useRef<HTMLButtonElement>(null)
  const nearBottom = useRef(true)
  const location = useLocation()
  const { selection, blockers } = useAssistantPageSelection()
  // A panel that would sit under the launcher (map layers) hides the assistant; its conversation is kept.
  const blocked = blockers > 0
  const farmMatch = /^\/dashboard\/farms\/([0-9a-f-]{36})(?:\/|$)/i.exec(location.pathname)
  const farmId = farmMatch?.[1] ?? null
  const fieldId = selection?.farmId === farmId ? selection.fieldId : null
  const pageType = farmId ? 'farm' : location.pathname.includes('/fields') ? 'fields' : 'dashboard'
  const visible = open && !closeForNavigation && !blocked

  useEffect(() => { if (visible) input.current?.focus() }, [visible])
  useEffect(() => {
    if (visible && nearBottom.current && transcript.current) transcript.current.scrollTop = transcript.current.scrollHeight
  }, [messages, pending, error, visible])
  useEffect(() => {
    if (!footerElement) return
    const update = () => {
      const rect = footerElement.getBoundingClientRect()
      let lift = Math.max(0, Math.min(rect.height, window.innerHeight - Math.max(rect.top, 0)))
      const button = toggle.current?.getBoundingClientRect()
      if (button) {
        const baseBottom = window.innerWidth <= 600 ? 10 : 20
        for (const element of document.querySelectorAll('.map-zoom, .map-recenter, .map-banner')) {
          const obstacle = element.getBoundingClientRect()
          const projectedBottom = window.innerHeight - baseBottom - lift
          if (obstacle.width > 0 && button.left < obstacle.right && button.right > obstacle.left &&
              projectedBottom > obstacle.top && projectedBottom - button.height < obstacle.bottom)
            lift = Math.max(lift, window.innerHeight - baseBottom - obstacle.top + 12)
        }
      }
      setFooterLift(lift)
    }
    let frame = 0
    const schedule = () => {
      if (frame) return
      frame = window.requestAnimationFrame(() => { frame = 0; update() })
    }
    const observer = new ResizeObserver(schedule)
    observer.observe(footerElement)
    const workspace = document.getElementById('workspace')
    const changes = new MutationObserver(schedule)
    if (workspace) changes.observe(workspace, { childList: true, subtree: true })
    window.addEventListener('scroll', schedule, { passive: true })
    window.addEventListener('resize', schedule)
    update()
    return () => { observer.disconnect(); changes.disconnect(); window.cancelAnimationFrame(frame); window.removeEventListener('scroll', schedule); window.removeEventListener('resize', schedule) }
  }, [footerElement, location.pathname])

  async function ask(message: string, history: ChatMessage[], retry = false,
    requestContext = conversation.current, requestPage: PageContext = { pageType, farmId, fieldId }) {
    if (pending) return
    const text = message.trim()
    if (!text) return
    const body = text.length <= maxMessageChars ? requestBody(text, history, requestContext, requestPage) : null
    if (!body) {
      setError('This message is too long to send. Shorten it and try again.')
      setFailed(null)
      return
    }
    if (!retry) {
      setMessages(current => [...current, { id: nextId.current++, role: 'user' as const, content: text }].slice(-maxMessages))
      setDraft('')
      nearBottom.current = true
    }
    setError(null)
    setFailed(null)
    setPending(true)
    try {
      const response = await apiFetch<ChatResponse>('/assistant/chat', {
        method: 'POST',
        body,
        signal: AbortSignal.timeout(requestTimeoutMs),
      })
      conversation.current = receive(response)
      setMessages(current => [...current, {
        id: nextId.current++, role: 'assistant' as const, content: response.message, scenarioJson: response.scenarioJson,
      }].slice(-maxMessages))
    } catch (cause) {
      // An ended session drops the scenario reference; the next sign-in starts without it.
      const sessionEnded = cause instanceof ApiError && cause.status === 401
      if (sessionEnded) conversation.current = null
      setError(errorText(cause))
      setFailed(canRetry(cause) ? { message: text, history, context: sessionEnded ? null : requestContext, page: requestPage } : null)
    } finally { setPending(false) }
  }

  function send(event: FormEvent<HTMLFormElement>) { event.preventDefault(); void ask(draft, messages) }
  function handleKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) { event.preventDefault(); void ask(draft, messages) }
  }
  function close() { setOpen(false); toggle.current?.focus() }
  async function copyJson(id: number, json: string) {
    try {
      await navigator.clipboard.writeText(json)
      setCopiedId(id)
      window.setTimeout(() => setCopiedId(current => current === id ? null : current), 2000)
    } catch { setError('Could not copy JSON. Select and copy it from the expanded section.') }
  }

  return <div className={`assistant-widget ${closeForNavigation ? 'is-navigation-open' : ''} ${blocked ? 'is-blocked' : ''}`} aria-hidden={blocked || undefined}
    style={{ '--assistant-footer-lift': `${footerLift}px` } as CSSProperties}>
    <button ref={toggle} className="assistant-toggle" type="button" onClick={() => setOpen(value => !value)}
      aria-expanded={visible} aria-controls="assistant-dialog" aria-label={visible ? 'Close Polle Assistant' : 'Open Polle Assistant'}>
      {visible ? <X size={23} /> : <Bot size={24} />}<span>{visible ? 'Close' : 'Assistant'}</span>
    </button>
    {visible && <section id="assistant-dialog" className="assistant-panel" aria-labelledby="assistant-title"
      onKeyDown={event => { if (event.key === 'Escape') { event.stopPropagation(); close() } }}>
      <header className="assistant-heading"><div><span className="mono">POLLE / LOCAL ASSISTANT</span>
        <h2 id="assistant-title">Polle Assistant</h2><p>Your farm planning assistant</p></div>
        <button type="button" className="assistant-close" onClick={close} aria-label="Close chat"><X size={19} /></button></header>
      <div className="assistant-transcript" ref={transcript} role="log" aria-label="Conversation history" aria-live="polite"
        onScroll={event => { const el = event.currentTarget; nearBottom.current = el.scrollHeight - el.scrollTop - el.clientHeight < 80 }}>
        {messages.length === 0 && <div className="assistant-welcome"><span className="assistant-mark"><Bot size={24} /></span>
          <h3>Welcome to Polle Assistant.</h3><p>Ask which plan suits a field, and I'll suggest plans and compare them with your current plan. Say the period any way you like, or leave it out (next month, 5 years). I also know your farms, budgets, fields and planting history.</p>
          <div className="assistant-prompts">{examples.map(prompt => <button type="button" key={prompt} onClick={() => void ask(prompt, messages)} disabled={pending}>{prompt}<ArrowUp size={13} /></button>)}</div></div>}
        {messages.map(item => <div className={`assistant-message assistant-message-${item.role}`} key={item.id}>
          <span className="assistant-speaker">{item.role === 'user' ? 'You' : 'Polle'}</span>
          {item.role === 'assistant' ? <MessageText text={item.content} /> : <p>{item.content}</p>}
          {item.scenarioJson && <details className="assistant-json"><summary>Show JSON</summary>
            <div className="assistant-json-actions"><button type="button" onClick={() => void copyJson(item.id, item.scenarioJson!)}>
              {copiedId === item.id ? <Check size={13} /> : <Copy size={13} />}{copiedId === item.id ? 'Copied' : 'Copy JSON'}</button></div>
            <pre><code>{item.scenarioJson}</code></pre></details>}</div>)}
        {pending && <div className="assistant-message assistant-message-assistant" role="status"><span className="assistant-speaker">Polle</span><p className="assistant-thinking">Preparing a response…</p></div>}
        {error && <div className="assistant-error" role="alert"><p>{error}</p>{failed && <button type="button" onClick={() => void ask(failed.message, failed.history, true, failed.context, failed.page)} disabled={pending}><RotateCcw size={14} /> Retry</button>}</div>}
      </div>
      <form className="assistant-compose" onSubmit={send}><label className="sr-only" htmlFor="assistant-input">Your message</label>
        <textarea ref={input} id="assistant-input" placeholder="Ask about your farm..." rows={2} maxLength={2000} value={draft}
          onChange={event => setDraft(event.target.value)} onKeyDown={handleKeyDown} disabled={pending} />
        <button type="submit" disabled={pending || !draft.trim()} aria-label="Send message"><ArrowUp size={18} /></button></form>
      <p className="assistant-footnote">Enter to send · Shift+Enter for a new line · Conversation stays in this tab</p>
    </section>}
  </div>
}
