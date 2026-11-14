import { rememberLastRun } from '@/features/strategies/lastRun'

/**
 * What the conversation has established, sent back with every message: the last tool, its farm, the period it used and,
 * after a strategy analysis, the field and its run. The API made it and checks it again; the browser only carries it.
 */
export type ConversationContext = {
  lastTool: 'prepare_solver_scenario' | 'get_farm_summary' | 'analyze_field_strategies'
  farmId: string
  startMonth: string | null
  planningHorizonYears: number | null
  fieldId?: string | null
  runId?: string | null
}
export type ChatMessage = { id: number; role: 'user' | 'assistant'; content: string; scenarioJson?: string | null }
export type ChatResponse = {
  message: string; conversationContext: ConversationContext | null; scenarioJson: string | null
  /** A strategy run the answer read or started for `fieldId`, shown on that field's Plan & strategies. */
  strategyRunId?: string | null; fieldId?: string | null
}
export type PageContext = { pageType: string; farmId: string | null; fieldId: string | null }

// Mirrors AssistantLimits in the API. History is bounded in UTF-8 bytes and the whole body stays below
// the API's 16 KB request limit.
export const maxMessageChars = 2000
export const maxHistoryItems = 6
const maxHistoryItemChars = 2000
const maxHistoryBytes = 8000
const maxRequestBytes = 15 * 1024
const encoder = new TextEncoder()
const byteLength = (value: string) => encoder.encode(value).length

function clip(content: string) {
  if (content.length <= maxHistoryItemChars) return content
  const clipped = content.slice(0, maxHistoryItemChars)
  return /[\uD800-\uDBFF]$/.test(clipped) ? clipped.slice(0, -1) : clipped
}

/**
 * The request for one message: the newest turns of the conversation that fit the limits (oldest dropped first), the page
 * the farmer is on, and the context the last answer returned, so "2026-10" and then "10" are read together. Null when even
 * the message alone is too large.
 */
export function requestBody(message: string, history: ChatMessage[], conversationContext: ConversationContext | null, pageContext: PageContext) {
  const recent: { role: ChatMessage['role']; content: string }[] = []
  let historyBytes = 0
  for (let index = history.length - 1; index >= 0 && recent.length < maxHistoryItems; index--) {
    const content = clip(history[index].content)
    historyBytes += byteLength(content)
    if (historyBytes > maxHistoryBytes) break
    recent.unshift({ role: history[index].role, content })
  }
  for (;;) {
    const body = JSON.stringify({ message, history: recent, pageContext, conversationContext })
    if (byteLength(body) <= maxRequestBytes) return body
    if (recent.length === 0) return null
    recent.shift()
  }
}

/**
 * Takes in an answer: the context to send with the next message, and a strategy run the assistant started or read
 * remembered as the field's last run, so its Plan & strategies opens on those cards.
 */
export function receive(response: ChatResponse, remember: (fieldId: string, runId: string) => void = rememberLastRun): ConversationContext | null {
  if (response.strategyRunId && response.fieldId) remember(response.fieldId, response.strategyRunId)
  return response.conversationContext
}
