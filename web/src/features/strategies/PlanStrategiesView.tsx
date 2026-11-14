import { useEffect, useEffectEvent, useRef, useState, type FormEvent, type KeyboardEvent } from 'react'
import { CalendarCheck, Maximize2, RotateCcw, Trash2, X } from 'lucide-react'
import type { Field } from '@/features/farms/api'
import { problemMessage } from '@/features/farms/api'
import { CropTimeline } from '@/features/farms/CropTimeline'
import { FieldTimeline } from '@/features/farms/FieldTimeline'
import type { Crop, Planting } from '@/features/farms/plantings'
import type { TimelineRange } from '@/features/farms/timeline'
import { ApiError } from '@/shared/api/client'
import { ErrorBoundary } from '@/shared/ui/ErrorBoundary'
import { TractorLoader, TractorSpinner } from '@/shared/ui/Tractor'
import { BASELINE_KEY, useApplyStrategy, useDeleteStrategy, useRetryJob, useSavedStrategies, useSaveStrategy, useStartStrategyRun, useStrategyPresets, useStrategyRun,
  type SavedStrategy, type StrategyResult } from './api'
import { useAuth } from '@/features/auth/useAuth'
import { DEFAULT_CHOICES, choiceCount, readChoices, requestChoices, writeChoices, type ChoiceState } from './choices'
import { StrategyChoicesDialog } from './StrategyChoicesDialog'
import { arrangeCards, badges, combineRuns, deltas, groupJobs, hasFigures, plannedLane, sortCards, strategyLane, type SortKey } from './compare'
import { defaultHorizon, monthText, NO_SOIL_MESSAGE, signedMoney } from './format'
import { StrategyCard } from './StrategyCard'
import { StrategyDetails } from './StrategyDetails'
import { MonthField } from './MonthField'
import { PlanningActions } from './PlanningActions'
import { forgetLastPlanRun, forgetLastRun, readLastPlanRun, readLastRun, rememberLastPlanRun, rememberLastRun } from './lastRun'
import '@/styles/strategies.css'

const SORTS: { key: SortKey; label: string }[] = [
  { key: 'profit', label: 'Highest profit' }, { key: 'water', label: 'Least water' }, { key: 'cost', label: 'Lowest cost' }, { key: 'risks', label: 'Fewest risks' },
]

const yearsRange =(startMonth: string, endMonth: string): TimelineRange => ({ start: `${startMonth.slice(0, 4)}-01-01`, end: `${endMonth.slice(0, 4)}-12-31` })

/** What Details shows; `savedId` or `jobKey` says where it came from, `confirming` asks before replacing the plan. */
type Details = { name: string; result: StrategyResult; baseline: StrategyResult | null; range: TimelineRange; startMonth: string; endMonth: string
  jobKey?: string; savedId?: string; confirming?: boolean; estimated?: boolean }

const isGone = (error: unknown) => error instanceof ApiError && error.status === 404

/**
 * Full-screen planning for one field: its crop plan, saved strategies, "Evaluate my plan", which asks the solver to score
 * only the current plan, and "Suggest strategies", which asks it only for the strategies. Each is its own run and neither
 * replaces the other; the cards fill in as each job finishes, and once the plan is scored for the strategies' months the
 * strategy cards compare with it, whichever button came first.
 */
export function PlanStrategiesView({ field, plantings, crops, onClose, openSavedId, onSavedOpened }: {
  field: Field; plantings: Planting[]; crops: Crop[]; onClose: () => void
  /** A saved strategy whose Details open as soon as the saved list loads (a link from the Planning view). */
  openSavedId?: string | null; onSavedOpened?: () => void
}) {
  const [horizon, setHorizon] = useState(defaultHorizon)
  // The field's last runs come back after a reload: the server keeps their cards, this browser keeps their ids.
  // `runId` is the last "Suggest strategies" run, `planRunId` the last "Evaluate my plan" run.
  const [runId, setRunId] = useState<string | null>(() => readLastRun(field.id))
  const [planRunId, setPlanRunId] = useState<string | null>(() => readLastPlanRun(field.id))
  const [sortBy, setSortBy] = useState<SortKey>('profit')
  // Where the current plan's crops without a harvest date end on its card.
  const [today] = useState(() => new Date().toISOString().slice(0, 10))
  const [details, setDetails] = useState<Details | null>(null)
  const [error, setError] = useState('')
  const start = useStartStrategyRun(field.id)
  const run = useStrategyRun(runId)
  const planRun = useStrategyRun(planRunId)
  const retry = useRetryJob()
  const saved = useSavedStrategies(field.id)
  const save = useSaveStrategy(field.id)
  const remove = useDeleteStrategy(field.id)
  const apply = useApplyStrategy(field)
  const [notice, setNotice] = useState('')
  const panel = useRef<HTMLElement>(null)
  // Which strategies a run plans: the presets by default, kept per account in this browser between visits. Another
  // account signed in here gets its own choices, and nothing is shown or kept until the account is known.
  const presets = useStrategyPresets()
  const presetKeys = (presets.data ?? []).map(preset => preset.key)
  const userId = useAuth().data?.id ?? null
  const [stored, setStored] = useState(() => ({ userId, choices: readChoices(userId) }))
  if (stored.userId !== userId) setStored({ userId, choices: readChoices(userId) })
  const choices = stored.userId === userId ? stored.choices : DEFAULT_CHOICES
  const [customizing, setCustomizing] = useState(false)
  function changeChoices(next: ChoiceState) {
    if (!userId) return
    setStored({ userId, choices: next })
    writeChoices(userId, next)
  }
  /** Closing with nothing chosen goes back to the presets rather than leaving a run with no strategies. */
  function closeChoices() {
    if (presets.data && choiceCount(choices, presetKeys) === 0) changeChoices(DEFAULT_CHOICES)
    setCustomizing(false)
  }

  const escape = useEffectEvent((event: globalThis.KeyboardEvent) => {
    // The add-crop drawer handles its own Escape; the strategy settings and the details overlay close before the view.
    if (event.key !== 'Escape' || document.querySelector('.catalog-drawer')) return
    if (customizing) closeChoices()
    else if (details) setDetails(null)
    else onClose()
  })
  useEffect(() => {
    const opener = document.activeElement as HTMLElement | null
    const listener = (event: globalThis.KeyboardEvent) => escape(event)
    document.addEventListener('keydown', listener)
    document.body.style.overflow = 'hidden'
    return () => { document.removeEventListener('keydown', listener); document.body.style.overflow = ''; requestAnimationFrame(() => opener?.isConnected && opener.focus()) }
  }, [])

  function trapTab(event: KeyboardEvent) {
    if (event.key !== 'Tab' || !panel.current || document.querySelector('.catalog-drawer')) return
    const focusable = [...panel.current.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), select:not(:disabled), summary, [href]')]
      .filter(element => element.offsetParent !== null)
    const first = focusable[0], last = focusable.at(-1)
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus() }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus() }
  }

  /** Runs only the chosen strategies; the plan's own evaluation, if any, stays and is compared with them. */
  async function suggest(event: FormEvent) {
    event.preventDefault()
    if (start.isPending || !field.soilAvailable || !userId) return
    setError('')
    try {
      // "All presets" plus custom strategies has to name the presets, so wait for them rather than send the custom ones alone.
      const available = presets.data ? presetKeys : choices.presets === null && choices.custom.length
        ? ((await presets.refetch({ throwOnError: true })).data ?? []).map(preset => preset.key)
        // A saved choice of presets goes out as it is; the API refuses one it no longer has.
        : choices.presets ?? []
      const created = await start.mutateAsync({ ...horizon, strategies: requestChoices(choices, available) })
      rememberLastRun(field.id, created.id)
      setRunId(created.id)
    } catch (reason) { setError(problemMessage(reason)) }
  }

  /** Scores only the farmer's own plan over the horizon: no strategies, so it's quicker, needs no choices and keeps the strategies' cards. */
  async function scorePlan() {
    if (start.isPending || !field.soilAvailable || !userId) return
    setError('')
    try {
      const created = await start.mutateAsync({ ...horizon, planOnly: true })
      rememberLastPlanRun(field.id, created.id)
      setPlanRunId(created.id)
    } catch (reason) { setError(problemMessage(reason)) }
  }

  // A remembered run the server no longer has for this account (deleted, or another user signed in): start empty.
  const runGone = isGone(run.error)
  const planGone = isGone(planRun.error)
  useEffect(() => {
    if (!runGone) return
    forgetLastRun(field.id)
    setRunId(null)
  }, [runGone, field.id])
  useEffect(() => {
    if (!planGone) return
    forgetLastPlanRun(field.id)
    setPlanRunId(null)
  }, [planGone, field.id])

  // The plan's card (from "Evaluate my plan") and the strategies (from "Suggest strategies"), compared when both are there
  // for the same months; strategies are compared only with a plan the solver scored, an unscored one still gets its card.
  const { planJob, planRun: planSource, strategyJobs, baseline, otherMonths } = combineRuns(planRun.data, run.data)
  const hasStrategies = strategyJobs.length > 0
  const ready = strategyJobs.filter(job => job.state !== 'processing').length
  const evaluating = planJob?.state === 'processing'
  // Strategies that land on the same plan show as one card, listing the others as its variations.
  const { cards, variations } = groupJobs(strategyJobs)
  // Strategies with something to compare are drawn; those that failed or found no plan are named in one line instead.
  const { shown: shownStrategies, failed, noPlan } = arrangeCards(cards)
  // The current plan is always drawn, scored or not, so it can be compared with the strategies.
  const shown = planJob ? [planJob, ...shownStrategies] : shownStrategies
  const won = badges(shown)
  const merged = strategyJobs.length - cards.length
  const scoringPlan = start.isPending && start.variables?.planOnly === true
  const [retryingAll, setRetryingAll] = useState(false)
  /** One at a time: each answer is the whole run, so parallel answers could overwrite each other's progress. */
  async function retryAll() {
    if (!runId) return
    setError('')
    setRetryingAll(true)
    try { for (const job of failed) await retry.mutateAsync({ runId, key: job.key }) }
    catch (reason) { setError(problemMessage(reason)) }
    finally { setRetryingAll(false) }
  }
  /** The run a card belongs to: the plan's card to its evaluation, the others to the strategies. */
  const runOf = (key: string) => key === BASELINE_KEY ? planSource! : run.data!
  const savedKeys = new Set((saved.data ?? []).filter(item => runId && item.savedAtUtc >= (run.data?.createdAtUtc ?? '')).map(item => item.key))
  // A strategy is saved with the plan's score it was compared with, so the saved list can show the difference later.
  const comparedPlanRunId = baseline && planSource && planSource.id !== runId ? planSource.id : null

  async function saveJob(key: string) {
    if (!runId) return
    setError('')
    try { await save.mutateAsync({ runId, jobKey: key, planRunId: comparedPlanRunId }) } catch (reason) { setError(problemMessage(reason)) }
  }

  const savedDetails = (item: SavedStrategy, confirming = false): Details | null => item.result
    ? { name: item.name, result: item.result, baseline: item.baseline, range: yearsRange(item.startMonth, item.endMonth), startMonth: item.startMonth, endMonth: item.endMonth, savedId: item.id, confirming } : null

  /** Saves the strategy first when it came from a run card, then replaces the field's plan from its start month on. */
  async function adoptPlan(target: Details) {
    setError('')
    try {
      const id = target.savedId ?? (runId && target.jobKey ? (await save.mutateAsync({ runId, jobKey: target.jobKey, planRunId: comparedPlanRunId })).id : null)
      if (!id) return
      await apply.mutateAsync(id)
      setDetails(null)
      setNotice(`Your crop plan now follows ${target.name} from ${monthText(target.startMonth)}.`)
    } catch (reason) { setError(problemMessage(reason)); setDetails(null) }
  }
  const openSaved = useEffectEvent((item: SavedStrategy | undefined) => {
    const target = item && savedDetails(item)
    if (target) setDetails(target)
    onSavedOpened?.()
  })
  useEffect(() => {
    if (openSavedId && saved.data) openSaved(saved.data.find(item => item.id === openSavedId))
  }, [openSavedId, saved.data])

  const replacedCount = (startMonth: string) => plantings.filter(planting => planting.plantingDate >= `${startMonth}-01`).length

  return <section ref={panel} className="strategy-view" role="dialog" aria-modal="true" aria-labelledby="strategy-view-title" onKeyDown={trapTab}>
    <header className="strategy-view-head">
      <div><span className="strategy-kicker">Plan &amp; strategies</span><h2 id="strategy-view-title">{field.name}</h2></div>
      <button type="button" className="button button-quiet button-icon" aria-label="Close plan and strategies" onClick={onClose}><X size={16} /></button>
    </header>

    <div className="strategy-view-body">
      <ErrorBoundary label="the plan and strategies" resetKey={`${planRunId}|${runId}`} className="strategy-block">
      <section className="strategy-block">
        <CropTimeline field={field} plantings={plantings} crops={crops} disabled={false} title="Your crop plan" layout="rows" />
        {notice && <p className="strategy-success" role="status">{notice}</p>}
      </section>

      <section className="strategy-block" aria-labelledby="saved-strategies">
        <h3 id="saved-strategies"><span>Saved strategies</span>{saved.data?.length ? <span className="strategy-count"> ({saved.data.length})</span> : null}</h3>
        {saved.isPending ? <p className="strategy-note">Loading saved strategies…</p>
          : !saved.data?.length ? <p className="strategy-empty">No saved strategies yet. Suggest strategies to compare options for this field.</p>
          : <ul className="strategy-saved-list">{saved.data.map(item => {
              const change = item.result && item.baseline ? deltas(item.result, item.baseline) : null
              const open = savedDetails(item)
              return <li key={item.id}>
                <div><strong>{item.name}</strong><span className="strategy-note">{`${monthText(item.startMonth)} – ${monthText(item.endMonth)}`}</span></div>
                {item.result && <ErrorBoundary label={`the ${item.name} timeline`} resetKey={item.result}>
                  <FieldTimeline compact label={`${item.name} crops`} range={yearsRange(item.startMonth, item.endMonth)} lanes={[{ key: 'plan', bars: strategyLane(item.result) }]} />
                </ErrorBoundary>}
                <div className="strategy-saved-actions">
                  {change?.profit != null && <span className={`mono ${change.profit >= 0 ? 'is-good' : 'is-bad'}`}>{signedMoney(change.profit)}</span>}
                  {open && <button type="button" className="button button-quiet" onClick={() => setDetails(savedDetails(item, true))}><CalendarCheck size={14} /><span>Use this plan</span></button>}
                  {open && <button type="button" className="button button-quiet" onClick={() => setDetails(open)}><Maximize2 size={14} /><span>Details</span></button>}
                  <button type="button" className="button button-quiet button-icon" aria-label={`Delete ${item.name}`} title="Delete saved strategy"
                    disabled={remove.isPending} onClick={() => void remove.mutateAsync(item.id).catch(reason => setError(problemMessage(reason)))}><Trash2 size={14} /></button>
                </div>
              </li>
            })}</ul>}
      </section>

      <section className="strategy-block" aria-labelledby="suggest-strategies">
        <h3 id="suggest-strategies">Planning</h3>
        <form className="strategy-horizon" onSubmit={suggest}>
          <MonthField id="horizon-from" label="From" value={horizon.startMonth} onChange={value => setHorizon({ ...horizon, startMonth: value })} />
          <MonthField id="horizon-to" label="To" value={horizon.endMonth} onChange={value => setHorizon({ ...horizon, endMonth: value })} />
          <PlanningActions disabled={start.isPending || !field.soilAvailable || !userId} describedBy={field.soilAvailable ? undefined : 'strategy-no-soil'}
            suggesting={start.isPending && !scoringPlan} evaluating={scoringPlan} customizing={customizing}
            choiceLabel={!presets.data || !userId ? null : choices.presets === null && !choices.custom.length ? `${presetKeys.length} presets` : `${choiceCount(choices, presetKeys)} chosen strategies`}
            onEvaluate={() => void scorePlan()} onCustomize={() => userId && setCustomizing(true)} />
        </form>
        {customizing && <StrategyChoicesDialog presets={presets.data ?? []} loading={presets.isPending} choices={choices}
          onChange={changeChoices} onClose={closeChoices} />}
        {presets.error && customizing && <p className="field-error" role="alert">{problemMessage(presets.error)}</p>}
        {!field.soilAvailable && <p id="strategy-no-soil" className="field-error" role="status">{NO_SOIL_MESSAGE}</p>}
        {error && <p className="field-error" role="alert">{error}</p>}
        {/* A failed check of a run already on screen keeps its cards and polling; only a run that never loaded gets the alert. */}
        {run.error && !runGone && !run.data && <p className="field-error" role="alert">{problemMessage(run.error)}</p>}
        {planRun.error && !planGone && !planRun.data && <p className="field-error" role="alert">{problemMessage(planRun.error)}</p>}
        {((runId && run.isPending) || (planRunId && planRun.isPending)) && <p className="strategy-note" role="status">Loading your last strategies…</p>}

        {(hasStrategies || planJob) && <>
          {/* The plan's evaluation and the strategies run apart, so each has its own status line and tractor. Whole sentences
              as single text nodes inside keyed elements: the counts change every poll and a translator may have replaced the
              text, so React swaps elements instead of patching or removing bare text nodes. */}
          <div className="strategy-progress-group">
            {planJob && <div className="strategy-progress" role="status" aria-live="polite" aria-label="Your plan">
              {evaluating && <TractorLoader compact />}
              {evaluating ? <span key="evaluating"><strong>Evaluating your plan…</strong></span>
                : <span key="evaluated"><strong>Your plan is evaluated.</strong></span>}
              {planRun.error && !planGone && evaluating && <span key="stale" className="strategy-note">Couldn’t check on your plan just now. Trying again.</span>}
            </div>}
            {hasStrategies && <div className="strategy-progress" role="status" aria-live="polite" aria-label="Strategies">
              {ready < strategyJobs.length && <TractorLoader compact />}
              {ready < strategyJobs.length
                ? <span key="preparing"><strong>{`Preparing strategies, ${ready} of ${strategyJobs.length} ready.`}</strong> <span>Cards appear as soon as each one is ready.</span></span>
                : <span key="done"><strong>{`${shownStrategies.length} ${shownStrategies.length === 1 ? 'strategy' : 'strategies'} to compare.`}</strong>{merged > 0 &&
                    <span>{` ${merged} more ${merged === 1 ? 'strategy' : 'strategies'} came out the same and ${merged === 1 ? 'is' : 'are'} grouped as variations.`}</span>}</span>}
              {run.error && !runGone && ready < strategyJobs.length && <span key="stale" className="strategy-note">Couldn’t check on the strategies just now. Trying again.</span>}
            </div>}
          </div>
          {hasStrategies && <div className="strategy-sort" role="group" aria-label="Sort strategies by">
            <span className="strategy-note">Sort by:</span>
            {SORTS.map(sort => <button key={sort.key} type="button" className={`strategy-chip ${sortBy === sort.key ? 'is-on' : ''}`} aria-pressed={sortBy === sort.key}
              onClick={() => setSortBy(sort.key)}><span>{sort.label}</span></button>)}
          </div>}
          {(failed.length > 0 || noPlan.length > 0 || (hasStrategies && (!planJob || otherMonths))) && <div className="strategy-left-out">
            {hasStrategies && !planJob && <p>Evaluate my plan for these months to compare it with the strategies.</p>}
            {otherMonths && <p>{`Your plan was evaluated for ${monthText(planSource!.startMonth)} – ${monthText(planSource!.endMonth)}, the strategies for `
              + `${monthText(run.data!.startMonth)} – ${monthText(run.data!.endMonth)}. Evaluate your plan for the same months to compare.`}</p>}
            {noPlan.length > 0 && <p><strong>{noPlan.length === 1 ? 'This strategy doesn’t fit this field:' : 'These strategies don’t fit this field:'}</strong> <span>{noPlan.map(job => job.name).join(', ')}.</span></p>}
            {failed.length > 0 && <p><strong>Couldn’t be prepared:</strong> <span>{failed.map(job => job.name).join(', ')}.</span>
              <button type="button" className="button button-quiet" disabled={retryingAll || retry.isPending} onClick={() => void retryAll()}>
                {retryingAll ? <TractorSpinner size={14} /> : <RotateCcw size={14} />}<span>{failed.length === 1 ? 'Retry' : 'Retry all'}</span></button></p>}
          </div>}
          <div className="strategy-grid">
            {sortCards(shown, sortBy).map(job => {
              const own = runOf(job.key)
              const range = yearsRange(own.startMonth, own.endMonth)
              return <ErrorBoundary key={job.key} label={job.name} resetKey={job} className="strategy-card"><StrategyCard job={job} baseline={baseline} badges={won[job.key] ?? []} variations={variations[job.key] ?? []} range={range} areaHectares={field.areaHectares}
                plannedCrops={job.key === BASELINE_KEY ? plannedLane(plantings, crops, own.startMonth, own.endMonth, today) : undefined}
                saved={savedKeys.has(job.key)} busy={(save.isPending && save.variables?.jobKey === job.key) || (retry.isPending && retry.variables?.runId === own.id && retry.variables.key === job.key)}
                onSave={job.key === BASELINE_KEY ? undefined : () => void saveJob(job.key)}
                onRetry={() => void retry.mutateAsync({ runId: own.id, key: job.key }).catch(reason => setError(problemMessage(reason)))}
                onUse={job.key === BASELINE_KEY || !job.result ? undefined
                  : () => setDetails({ name: job.name, result: job.result!, baseline, range, startMonth: own.startMonth, endMonth: own.endMonth, jobKey: job.key, confirming: true })}
                onDetails={hasFigures(job.result) ? () => setDetails({ name: job.name, result: job.result!, baseline: job.key === BASELINE_KEY ? null : baseline, range, startMonth: own.startMonth, endMonth: own.endMonth, jobKey: job.key, estimated: job.estimated === true }) : undefined} /></ErrorBoundary>
            })}
          </div>
        </>}
      </section>
      </ErrorBoundary>
    </div>

    {details && <ErrorBoundary label={details.name} resetKey={details} className="strategy-details" onDismiss={() => setDetails(null)}><StrategyDetails name={details.name} result={details.result} baseline={details.baseline} range={details.range} horizon={{ startMonth: details.startMonth, endMonth: details.endMonth }} fieldId={field.id} areaHectares={field.areaHectares} estimated={details.estimated} onClose={() => setDetails(null)}
      notice={details.confirming && <div className="strategy-confirm" role="alertdialog" aria-labelledby="strategy-confirm-text">
        <p id="strategy-confirm-text">{replacedCount(details.startMonth)
          ? `Replace the ${replacedCount(details.startMonth)} ${replacedCount(details.startMonth) === 1 ? 'crop' : 'crops'} in your plan from ${monthText(details.startMonth)} on with these ${details.result.cycles.length}? Earlier crops stay.`
          : `Add these ${details.result.cycles.length} crops to your plan from ${monthText(details.startMonth)}? Earlier crops stay.`}</p>
        <div className="strategy-view-actions">
          <button type="button" className="button button-dark" disabled={apply.isPending || save.isPending} onClick={() => void adoptPlan(details)}>
            {apply.isPending ? <TractorSpinner size={14} /> : <CalendarCheck size={14} />}<span>{replacedCount(details.startMonth) ? 'Replace my plan' : 'Add to my plan'}</span></button>
          <button type="button" className="button button-quiet" onClick={() => setDetails({ ...details, confirming: false })}><span>Keep my plan</span></button>
        </div>
      </div>}
      actions={(details.savedId || (details.jobKey && details.jobKey !== BASELINE_KEY)) && <>
        {details.jobKey && !details.savedId && (savedKeys.has(details.jobKey)
          ? <span className="strategy-saved"><span>Saved</span></span>
          : <button type="button" className="button button-quiet" disabled={save.isPending} onClick={() => void saveJob(details.jobKey!)}><span>Save strategy</span></button>)}
        {!details.confirming && <button type="button" className="button button-dark" onClick={() => setDetails({ ...details, confirming: true })}><CalendarCheck size={14} /><span>Use this plan</span></button>}
      </>} /></ErrorBoundary>}
  </section>
}
