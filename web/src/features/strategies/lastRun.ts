/**
 * The field's last runs, kept in this browser so a reload (F5) shows the same cards again: one for "Suggest strategies"
 * (also the assistant's runs) and one for "Evaluate my plan", so neither button wipes what the other found. The runs and
 * their results live on the server (GET /strategy-runs/{id}), which answers only their owner; only the ids are stored
 * here. Storage can be missing or blocked (private windows, previews), so every access is guarded and the view simply
 * starts empty then.
 */
type Store = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>

const RUN_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const storageKey = (fieldId: string) => `polle.strategy-run.${fieldId}`
const planKey = (fieldId: string) => `polle.plan-run.${fieldId}`

function browserStore(): Store | null {
  try { return globalThis.localStorage ?? null } catch { return null }
}

function read(key: string, store: Store | null): string | null {
  try {
    const value = store?.getItem(key) ?? null
    return value && RUN_ID.test(value) ? value : null
  } catch { return null }
}

function write(key: string, runId: string, store: Store | null) {
  try { store?.setItem(key, runId) } catch { /* storage full or blocked: the cards just won't survive a reload */ }
}

function remove(key: string, store: Store | null) {
  try { store?.removeItem(key) } catch { /* nothing to clean up */ }
}

export const readLastRun = (fieldId: string, store: Store | null = browserStore()) => read(storageKey(fieldId), store)
export const rememberLastRun = (fieldId: string, runId: string, store: Store | null = browserStore()) => write(storageKey(fieldId), runId, store)
export const forgetLastRun = (fieldId: string, store: Store | null = browserStore()) => remove(storageKey(fieldId), store)

export const readLastPlanRun = (fieldId: string, store: Store | null = browserStore()) => read(planKey(fieldId), store)
export const rememberLastPlanRun = (fieldId: string, runId: string, store: Store | null = browserStore()) => write(planKey(fieldId), runId, store)
export const forgetLastPlanRun = (fieldId: string, store: Store | null = browserStore()) => remove(planKey(fieldId), store)
