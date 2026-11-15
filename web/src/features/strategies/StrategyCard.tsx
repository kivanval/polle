import { Fragment } from 'react'
import { AlertTriangle, CalendarCheck, Check, Layers, Maximize2, RotateCcw, Save } from 'lucide-react'
import { FieldTimeline, type TimelineBar } from '@/features/farms/FieldTimeline'
import type { TimelineRange } from '@/features/farms/timeline'
import { TractorSpinner } from '@/shared/ui/Tractor'
import { BASELINE_KEY, type StrategyJob, type StrategyResult } from './api'
import { baselineIssue, deltas, hasFigures, partlyEstimated, strategyLane, usable, whyText, type Badge } from './compare'
import { settingsText } from './details'
import { cubicMetres, cycleNotes, money, noteLines, signedCount, signedCubicMetres, signedMoney, tone } from './format'

type CardProps = {
  job: StrategyJob; baseline: StrategyResult | null; badges: Badge[]; range: TimelineRange; saved: boolean; busy: boolean
  /** Other strategies that came out with exactly this plan, grouped under this card. */
  variations?: StrategyJob[]
  /** The field's area, to show irrigation as a volume. */
  areaHectares: number
  /** The current plan only: the farmer's own crops in the horizon, drawn when the solver sent none back. */
  plannedCrops?: TimelineBar[]
  onSave?: () => void; onRetry?: () => void; onDetails?: () => void; onUse?: () => void
}

/** The plan's risks, warnings and plain notes under their own headings, folded until asked for. */
function NoteGroups({ result }: { result: StrategyResult }) {
  const notes = [...new Set(result.cycles.flatMap(cycleNotes))]
  const groups = [
    { key: 'risks', title: 'Risks', items: noteLines(result.risks) },
    { key: 'warnings', title: 'Warnings', items: noteLines(result.warnings ?? []) },
    { key: 'notes', title: 'Notes', items: notes },
  ].filter(group => group.items.length)
  if (!groups.length) return null
  const count = (key: string, one: string, many: string) => { const items = groups.find(group => group.key === key)?.items.length ?? 0; return items ? `${items} ${items === 1 ? one : many}` : null }
  return <details className="strategy-risks">
    <summary>{[count('risks', 'risk', 'risks'), count('warnings', 'warning', 'warnings'), count('notes', 'note', 'notes')].filter(Boolean).join(' · ')}</summary>
    {groups.map(group => <div key={group.key} className={`strategy-note-group is-${group.key}`}>
      <h5>{group.title}</h5>
      <ul>{group.items.map(item => <li key={item}><span>{item}</span></li>)}</ul>
    </div>)}
  </details>
}

/** One strategy (or the farmer's current plan) as a comparable card: pending, failed, without a plan, or ready. */
export function StrategyCard({ job, baseline, badges, variations = [], range, saved, busy, areaHectares, plannedCrops = [], onSave, onRetry, onDetails, onUse }: CardProps) {
  const isBaseline = job.key === BASELINE_KEY
  const issue = isBaseline ? baselineIssue(job) : null
  // The current plan's figures come wholly (estimated) or partly (crops the solver left out) from the API's audit.
  const estimated = isBaseline && (job.estimated === true || partlyEstimated(job))
  const flagged = isBaseline && job.result ? job.result.risks.length + (job.result.warnings?.length ?? 0) : 0
  // The card's four parts (head, numbers, explanation, actions) sit on the strategy grid's shared rows, so every
  // card in a row starts its numbers and its buttons at the same height however long its head or "Why" runs.
  const head = <div className="strategy-card-part">
    <div className="strategy-badges">
      {isBaseline && <span className="strategy-badge is-own">Your plan</span>}
      {issue && !estimated && <span className="strategy-badge is-alert"><AlertTriangle size={11} aria-hidden="true" /><span>Not scored</span></span>}
      {estimated && <span className="strategy-badge is-caution"><span>{job.estimated ? 'Estimated' : 'Partly estimated'}</span></span>}
      {(!issue || estimated) && flagged > 0 && <span className="strategy-badge is-caution"><AlertTriangle size={11} aria-hidden="true" /><span>{`${flagged} to review`}</span></span>}
      {badges.map(badge => <span key={badge} className="strategy-badge">{badge}</span>)}
    </div>
    <h4>{job.name}</h4>
    <p className="strategy-summary">{job.summary}</p>
    {job.settings && <ul className="strategy-settings" aria-label={isBaseline ? 'How your plan is scored' : 'What this plan may do'}>
      {settingsText(job.settings).map(text => <li key={text}><span>{text}</span></li>)}
    </ul>}
    {variations.length > 0 && <details className="strategy-variations">
      <summary><Layers size={12} aria-hidden="true" /><span>{`${variations.length + 1} variations`}</span></summary>
      <p>{`${[...variations.map(variation => variation.name), job.name].join(', ').replace(/, ([^,]*)$/, ' and $1')} came out with this same plan, so they share this card.`}</p>
      <ul>{variations.map(variation => <li key={variation.key}><strong>{variation.name}</strong> <span>{variation.summary}</span></li>)}</ul>
    </details>}
  </div>

  if (job.state === 'processing') return <article className="strategy-card is-pending" aria-busy="true">
    {head}<div className="strategy-card-part"><span className="strategy-skeleton" /><span className="strategy-skeleton" /><span className="strategy-skeleton is-short" /></div>
  </article>

  // A failed job, and also a state or a missing result this app does not expect: never read as a ready plan.
  if (job.state !== 'ready' || !job.result) return <article className={`strategy-card ${isBaseline ? 'is-baseline' : ''}`}>
    {head}
    <p className="strategy-problem" role="status">{isBaseline ? 'Your plan couldn’t be scored just now.' : 'This plan couldn\'t be prepared.'}</p>
    {onRetry && job.state === 'failed' && <div className="strategy-actions"><button type="button" className="button button-quiet" disabled={busy} onClick={onRetry}>
      {busy ? <TractorSpinner size={14} /> : <RotateCcw size={14} />}<span>Retry</span></button></div>}
  </article>

  const result = job.result
  const issueNote = issue && <p className="strategy-issue" role="note"><AlertTriangle size={14} aria-hidden="true" /><span>{issue}</span></p>
  // The current plan keeps a full card whatever the solver made of it; without crops back, it shows the farmer's own.
  if (isBaseline && !hasFigures(result)) return <article className="strategy-card is-baseline is-unscored">
    {head}
    <div className="strategy-card-part">
      {plannedCrops.length > 0 && <FieldTimeline compact label={`${job.name} crops`} range={range} lanes={[{ key: 'plan', bars: plannedCrops }]} />}
      <dl className="strategy-metrics">
        {['Profit', 'Costs', 'Irrigation', 'Risks to review', 'Warnings'].map(label => <Fragment key={label}><dt>{label}</dt><dd className="is-unknown"><span>—</span></dd></Fragment>)}
      </dl>
    </div>
    <div className="strategy-card-part">{issueNote}<NoteGroups result={result} /></div>
    <div className="strategy-actions" />
  </article>
  if (!usable(job) && !isBaseline) return <article className="strategy-card">
    {head}<p className="strategy-problem">This plan doesn’t fit this field with these limits.</p>
  </article>

  const change = isBaseline ? null : deltas(result, baseline)
  const metric = (label: string, absolute: string, delta: string | null, toneClass: string) =>
    <><dt>{label}</dt><dd className={change ? toneClass : ''}><span>{change && delta !== null ? delta : absolute}</span></dd></>

  return <article className={`strategy-card ${isBaseline ? 'is-baseline' : ''}`}>
    {head}
    <div className="strategy-card-part">
      <FieldTimeline compact label={`${job.name} crops`} range={range} lanes={[{ key: 'plan', bars: strategyLane(result) }]} />
      {/* Each value sits in its own element so a translator rewriting the text can't strand React's text nodes. */}
      <dl className="strategy-metrics">
        {metric('Profit', money(result.profitUsd), change?.profit != null ? signedMoney(change.profit) : null, tone(change?.profit ?? null, true))}
        {metric('Costs', money(result.costsUsd), change ? signedMoney(change.costs) : null, tone(change?.costs ?? null, false))}
        {metric('Irrigation', cubicMetres(result.irrigationMm, areaHectares), change ? signedCubicMetres(change.irrigation, areaHectares) : null, tone(change?.irrigation ?? null, false))}
        {metric('Risks to review', String(result.risks.length), change ? signedCount(change.risks) : null, tone(change?.risks ?? null, false))}
        {metric('Warnings', String(result.warnings?.length ?? 0), change ? signedCount(change.warnings) : null, tone(change?.warnings ?? null, false))}
      </dl>
      {change && <p className="strategy-note">Compared with your current plan.</p>}
    </div>
    <div className="strategy-card-part">
      {!isBaseline && <p className="strategy-why"><strong>Why:</strong> <span>{whyText(result, baseline, job.settings)}</span></p>}
      {issueNote}
      <NoteGroups result={result} />
      {result.risks.length + (result.warnings?.length ?? 0) > 0 && <p className="strategy-note">Hover a crop to see its risks and warnings. Details lists them by crop.</p>}
    </div>
    <div className="strategy-actions">
      {onSave && (saved
        ? <span className="strategy-saved"><Check size={14} /><span>Saved</span></span>
        : <button type="button" className="button button-dark" disabled={busy} onClick={onSave}>{busy ? <TractorSpinner size={14} /> : <Save size={14} />}<span>Save plan</span></button>)}
      {onUse && <button type="button" className="button button-quiet" disabled={busy} onClick={onUse}><CalendarCheck size={14} /><span>Use this plan</span></button>}
      {onDetails && <button type="button" className="button button-quiet" onClick={onDetails}><Maximize2 size={14} /><span>Details</span></button>}
    </div>
  </article>
}
