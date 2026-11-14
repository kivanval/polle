import { describe, expect, it, vi } from 'vitest'
import { maxHistoryItems, receive, requestBody, type ChatMessage, type ConversationContext } from './chat'

const page = { pageType: 'farm', farmId: 'farm-1', fieldId: 'field-1' }
const turn = (id: number, role: ChatMessage['role'], content: string): ChatMessage => ({ id, role, content })

describe('the assistant conversation', () => {
  it('sends the earlier turns and the established context with every message, so "2026-10" then "10" are read together', () => {
    const context: ConversationContext = { lastTool: 'prepare_solver_scenario', farmId: 'farm-1', startMonth: '2026-11', planningHorizonYears: 5 }
    const history = [turn(1, 'user', '2026-10'), turn(2, 'assistant', '2026-10 has passed, so I start in 2026-11; no length given, so I use 5 years.')]

    const body = JSON.parse(requestBody('10', history, context, page)!)

    expect(body.message).toBe('10')
    expect(body.history).toEqual([{ role: 'user', content: '2026-10' }, { role: 'assistant', content: history[1].content }])
    expect(body.conversationContext).toEqual(context)
    expect(body.pageContext).toEqual(page)
  })

  it('keeps the field and run of a strategy analysis for the follow-up', () => {
    const context: ConversationContext = { lastTool: 'analyze_field_strategies', farmId: 'farm-1', startMonth: '2026-12', planningHorizonYears: 5,
      fieldId: 'field-1', runId: 'run-1' }

    const body = JSON.parse(requestBody('And for 10 years?', [], context, page)!)

    expect(body.conversationContext).toMatchObject({ lastTool: 'analyze_field_strategies', fieldId: 'field-1', runId: 'run-1' })
  })

  it('sends the newest turns when the conversation is long', () => {
    const history = Array.from({ length: 10 }, (_, index) => turn(index, index % 2 ? 'assistant' : 'user', `message ${index}`))

    const body = JSON.parse(requestBody('next', history, null, page)!)

    expect(body.history).toHaveLength(maxHistoryItems)
    expect(body.history.at(-1).content).toBe('message 9')
  })

  it('takes the next context from the answer and remembers a strategy run on its field', () => {
    const remember = vi.fn()
    const context: ConversationContext = { lastTool: 'analyze_field_strategies', farmId: 'farm-1', startMonth: '2026-12', planningHorizonYears: 5,
      fieldId: 'field-1', runId: '8f3a1f0e-1111-4222-8333-944445555666' }

    const next = receive({ message: 'Strategies for field North…', conversationContext: context, scenarioJson: null,
      strategyRunId: context.runId, fieldId: 'field-1' }, remember)

    expect(next).toBe(context)
    expect(remember).toHaveBeenCalledWith('field-1', context.runId)
  })

  it('remembers nothing for an answer without a run', () => {
    const remember = vi.fn()

    expect(receive({ message: 'Hello!', conversationContext: null, scenarioJson: null }, remember)).toBeNull()
    expect(remember).not.toHaveBeenCalled()
  })
})
