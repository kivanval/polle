import { useEffect, useRef, useState, type KeyboardEvent } from 'react'
import { Droplets, FlaskConical, X } from 'lucide-react'
import type { TimelineRange } from '@/features/farms/timeline'
import type { CostBreakdown, StrategyCycle, StrategyResult } from './api'
import { ESTIMATED_PLAN_TEXT } from './compare'
import { agronomicRisks, cycleRow, expectedYield, summarySteps, type FertilizerPlan, type WaterHorizon, type WaterPlan } from './details'
import { FoldButton } from './Fold'
import { useFold } from './useFold'
import { categoryText, cubicMetres, cycleNotes, cycleRisks, cycleWarnings, money, dateText, harvestDays, signedMoney, tone, whenText } from './format'
import { NutrientChart, WaterBalanceChart } from './StrategyBalance'
import { StrategyGantt } from './StrategyGantt'

const KIND_TAG = { risk: 'Risk', warning: 'Warning', note: 'Note' } as const

/**
 * A crop's risks, then warnings, then plain notes, each tagged; a category other than the everyday agronomic one (missing
 * data, soil health, or one the solver adds later) joins the tag.
 */
function noteRows(cycle: StrategyCycle) {
  const category = (text: string) => cycle.noteDetails?.find(detail => detail.message.trim() === text)?.category ?? 'agronomic'
  const tag = (kind: keyof typeof KIND_TAG, text: string) => {
    const extra = category(text)
    return extra === 'agronomic' ? KIND_TAG[kind] : `${KIND_TAG[kind]} · ${extra === 'data_missing' ? 'Missing data' : categoryText(extra)}`
  }
  return [
    ...cycleRisks(cycle).map(text => ({ kind: 'risk' as const, text })),
    ...cycleWarnings(cycle).map(text => ({ kind: 'warning' as const, text })),
    ...cycleNotes(cycle).map(text => ({ kind: 'note' as const, text })),
  ].map(row => ({ ...row, tag: tag(row.kind, row.text) }))
}

const kgFormat = new Intl.NumberFormat('en', { maximumFractionDigits: 0 })
const hectareFormat = new Intl.NumberFormat('en', { maximumFractionDigits: 1 })
/** Kilograms of product, always in kg: the unit fertilizer is bought and spread in. */
const kilograms = (kg: number) => `${kgFormat.format(Math.round(kg))} kg`
const hectares = (value: number) => `${hectareFormat.format(value)} ha`

/** Harvest months in words; a perennial's long run of yearly harvests reads as first – last with the count. */
function harvestText(months: string[]) {
  if (!months.length) return '—'
  if (months.length <= 3) return months.map(dateText).join(', ')
  return `${dateText(months[0])} – ${dateText(months.at(-1)!)}, ${months.length} harvests`
}

const tonneFormat = new Intl.NumberFormat('en', { maximumFractionDigits: 2 })
/** A ton or more also reads in tonnes, e.g. "82.24 t". */
const tonnes = (kg: number) => kg >= 1000 ? `${tonneFormat.format(kg / 1000)} t` : null

/**
 * A catalog name ending in its N : P : K grade, e.g. "Nitrophoska: Grade A (16.5 : 16.5 : 16.5)", split into the
 * name and the three numbers; names without one keep their text and get no grade.
 */
function productGrade(product: string): { name: string; grade: [string, string, string] | null } {
  const match = /^(.*?)\s*\(\s*(\d+(?:[.,]\d+)?)\s*[:/]\s*(\d+(?:[.,]\d+)?)\s*[:/]\s*(\d+(?:[.,]\d+)?)\s*\)\s*$/.exec(product)
  return match && match[1] ? { name: match[1], grade: [match[2], match[3], match[4]] } : { name: product, grade: null }
}

/**
 * The same framed card as a water or fertilizer purchase, for a crop that needs none, so every crop shows a matching
 * pair of cards.
 */
function EmptyResource({ icon, title, text, water = false }: { icon: React.ReactNode; title: string; text: string; water?: boolean }) {
  return <div className={`strategy-fertilizer-card is-empty${water ? ' is-water' : ''}`}>
    <span className="strategy-fertilizer-icon" aria-hidden="true">{icon}</span>
    <div className="strategy-fertilizer-body">
      <p className="strategy-fertilizer-action"><strong>{title}</strong></p>
      <p className="strategy-fertilizer-rate">{text}</p>
    </div>
  </div>
}

const percentFormat = new Intl.NumberFormat('en', { maximumFractionDigits: 0 })
const percentText = (value: number) => `${percentFormat.format(value)}%`

/**
 * The fertilizer to buy for one crop as its own small card: the kilograms of product for the whole field, the product
 * with its N:P:K grade, the per-hectare rate it comes from, and the target the solver doses to (the share of the crop's
 * nutrient deficit it covers). Results from before the solver sent the amount name the product only.
 */
function FertilizerActions({ plan, areaHectares }: { plan: FertilizerPlan | null; areaHectares: number }) {
  if (!plan) return <EmptyResource icon={<FlaskConical size={18} strokeWidth={1.8} />} title="No fertilizer needed"
    text="Soil nutrient levels are sufficient for this crop" />
  const { name, grade } = productGrade(plan.product)
  const total = plan.rateKgHa === null ? null : areaHectares > 0 ? plan.rateKgHa * areaHectares : plan.rateKgHa
  return <div className="strategy-fertilizer-card">
    <span className="strategy-fertilizer-icon" aria-hidden="true"><FlaskConical size={18} strokeWidth={1.8} /></span>
    <div className="strategy-fertilizer-body">
      <p className="strategy-fertilizer-action">
        {total === null
          ? <span>Fertilizer</span>
          : <><span>Buy </span><strong>{kilograms(total)}</strong>{tonnes(total) && <span className="strategy-fertilizer-tonnes">{` (${tonnes(total)})`}</span>}</>}
      </p>
      <p className="strategy-fertilizer-product"><strong>{name}</strong>
        {grade && <span className="strategy-npk" title={`Nitrogen ${grade[0]}%, phosphorus ${grade[1]}%, potassium ${grade[2]}%`}>
          <span className="strategy-npk-label">N:P:K</span> <span>{grade.join('/')}</span></span>}</p>
      {plan.targetPercent !== null && <p className="strategy-resource-target"><span>Target: </span>
        <strong>{`cover ${percentText(plan.targetPercent)} of the nutrient shortfall`}</strong></p>}
      <p className="strategy-fertilizer-rate">
        {plan.rateKgHa === null ? 'The amount isn’t in this result. Suggest plans again to see it.'
          : areaHectares > 0 ? `${kgFormat.format(Math.round(plan.rateKgHa))} kg/ha × ${hectares(areaHectares)}` : 'per hectare'}</p>
    </div>
  </div>
}

/**
 * The irrigation water for one crop as a card beside its fertilizer: the volume for the whole field in m³, the depth in
 * mm it comes from, and the level the solver waters to (how far each watering refills the water the roots can easily
 * take up). Nothing to buy for a rain-fed crop.
 */
function WaterActions({ plan, areaHectares }: { plan: WaterPlan | null; areaHectares: number }) {
  if (!plan) return <EmptyResource water icon={<Droplets size={18} strokeWidth={1.8} />} title="No irrigation needed"
    text="Rainfed — natural rainfall covers this crop" />
  return <div className="strategy-fertilizer-card is-water">
    <span className="strategy-fertilizer-icon" aria-hidden="true"><Droplets size={18} strokeWidth={1.8} /></span>
    <div className="strategy-fertilizer-body">
      <p className="strategy-fertilizer-action">
        {areaHectares > 0
          ? <><span>Irrigate </span><strong>{cubicMetres(plan.mm, areaHectares)}</strong><span className="strategy-fertilizer-tonnes">{` (${mmFormat.format(plan.mm)} mm)`}</span></>
          : <><span>Irrigate </span><strong>{`${mmFormat.format(plan.mm)} mm`}</strong></>}
      </p>
      {plan.targetPercent !== null && <p className="strategy-resource-target"><span>Target: </span>
        <strong>{`refill the root-zone water to ${percentText(plan.targetPercent)} each time`}</strong></p>}
      <p className="strategy-fertilizer-rate">
        {areaHectares > 0 ? `${mmFormat.format(plan.mm)} mm over the season × ${hectares(areaHectares)} (1 mm on 1 ha is 10 m³)` : 'over the season'}</p>
    </div>
  </div>
}

const mmFormat = new Intl.NumberFormat('en', { maximumFractionDigits: 0 })

const COST_PARTS = [['operatingUsd', 'Machinery & labour'], ['plantingUsd', 'Seed'], ['waterUsd', 'Water'], ['fertilizerUsd', 'Fertilizer']] as const

/**
 * What the costs are made of, as label-over-number cells like the totals beside it (machinery & labour, seed, water,
 * fertilizer); parts that cost nothing are left out. `className` picks the look: the summary's totals or a crop's facts.
 */
function CostParts({ breakdown, label, className }: { breakdown: CostBreakdown | null | undefined; label: string; className: string }) {
  const parts = breakdown ? COST_PARTS.filter(([key]) => breakdown[key] > 0) : []
  if (!breakdown || !parts.length) return null
  return <dl className={`${className} strategy-cost-parts`} aria-label={label}>{parts.map(([key, name]) =>
    <div key={key}><dt>{name}</dt><dd className="mono">{money(breakdown[key])}</dd></div>)}</dl>
}

/** How many risks, warnings and notes a crop has, e.g. "3 risks · 1 warning". */
function notesCount(notes: { kind: keyof typeof KIND_TAG }[]) {
  return (['risk', 'warning', 'note'] as const).flatMap(kind => {
    const count = notes.filter(note => note.kind === kind).length
    return count ? [`${count} ${KIND_TAG[kind].toLowerCase()}${count === 1 ? '' : 's'}`] : []
  }).join(' · ')
}

/**
 * The strategy as a step-by-step plan in one white card: for each crop when to sow and harvest, what it costs and
 * earns, what fertilizer goes on when, and its risks and notes.
 */
function StrategySummary({ result, areaHectares }: { result: StrategyResult; areaHectares: number }) {
  const steps = summarySteps(result.cycles)
  const fold = useFold()
  if (!steps.length) return null
  const profit = result.profitUsd ?? result.revenueUsd - result.costsUsd
  // An itemised result lists its costs under the totals; older ones split them into inputs and operations only.
  const operating = result.costBreakdown ? null : result.operatingCostsUsd ?? null
  return <section className="balance-panel strategy-summary" aria-labelledby="strategy-summary-title">
    <div className="strategy-summary-head">
      <h3 id="strategy-summary-title"><FoldButton open={fold.open} onToggle={fold.toggle}>Summary</FoldButton></h3>
      <dl className="balance-totals">
        {/* Inputs are seed, water and fertilizer; operations are machinery and labour. Costs is their sum. */}
        {operating !== null && <div><dt>Inputs</dt><dd className="mono">{money(result.costsUsd - operating)}</dd></div>}
        {operating !== null && <div><dt>Operations</dt><dd className="mono">{money(operating)}</dd></div>}
        <div><dt>{operating !== null || result.costBreakdown ? 'Total costs' : 'Costs'}</dt><dd className="mono">{money(result.costsUsd)}</dd></div>
        <div><dt>Revenue</dt><dd className="mono">{money(result.revenueUsd)}</dd></div>
        <div><dt>Profit</dt><dd className={`mono ${tone(profit, true)}`}>{signedMoney(profit)}</dd></div>
      </dl>
    </div>
    {fold.open && <>
    <CostParts breakdown={result.costBreakdown} label="Total costs by kind" className="balance-totals" />
    <ol className="strategy-steps">
      {steps.map((step, index) => { const { cycle } = step, notes = noteRows(cycle); return <li key={step.key} className="strategy-step">
        <span className="strategy-step-number" aria-hidden="true">{index + 1}</span>
        <div className="strategy-step-body">
          <h4 className="strategy-step-title"><strong>{cycle.cropName}</strong>
            {cycle.systemName && <span className="strategy-table-sub">{cycle.systemName}</span>}
            {cycle.estimated && <span className="strategy-badge is-caution">Estimated</span>}
            <span className="strategy-step-period">{`${whenText(cycle.plantingDate, cycle.plantingMonth)} → ${whenText(cycle.clearanceDate, cycle.clearanceMonth)}`}</span></h4>
          <dl className="strategy-step-facts">
            <div><dt>Sow</dt><dd>{whenText(cycle.plantingDate, cycle.plantingMonth)}</dd></div>
            <div><dt>Harvest</dt><dd>{harvestText(harvestDays(cycle))}</dd></div>
            <div><dt>Field clear</dt><dd>{whenText(cycle.clearanceDate, cycle.clearanceMonth)}</dd></div>
            <div><dt>Costs</dt><dd className="mono">{money(cycle.costsUsd)}</dd></div>
            {cycle.operatingCostsUsd != null && !cycle.costBreakdown && <div><dt>of which operations</dt><dd className="mono">{money(cycle.operatingCostsUsd)}</dd></div>}
            <div><dt>Revenue</dt><dd className="mono">{money(cycle.revenueUsd)}</dd></div>
            <div><dt>Profit</dt><dd className={`mono ${tone(step.netUsd, true)}`}>{signedMoney(step.netUsd)}</dd></div>
            {expectedYield(cycle.yieldPercent) && <div title="The share of this crop's full yield the planner expects after dry spells, nutrient shortfalls and other stresses">
              <dt>Expected yield</dt><dd className="strategy-yield">{expectedYield(cycle.yieldPercent)}</dd></div>}
          </dl>
          <CostParts breakdown={cycle.costBreakdown} label={`${cycle.cropName} costs by kind`} className="strategy-step-facts" />
          <div className="strategy-resource-cards">
            <WaterActions plan={step.water} areaHectares={areaHectares} />
            <FertilizerActions plan={step.fertilizer} areaHectares={areaHectares} />
          </div>
          {/* Folded by default so a long plan stays short; each note's text in its own span, so a page translator
              rewriting it can't break React's updates. */}
          {notes.length > 0 && <details className="strategy-step-notes">
            <summary><span>{notesCount(notes)}</span></summary>
            <ul className="strategy-notes">{notes.map(note => <li key={`${note.kind}-${note.text}`} className={`is-${note.kind}`}>
              <span className={`strategy-note-tag is-${note.kind}`}>{note.tag}</span> <span>{note.text}</span></li>)}</ul>
          </details>}
        </div>
      </li> })}
    </ol>
    </>}
  </section>
}

/**
 * Every crop's agronomic risks and warnings in one open panel (drought, sowing month, rotation, soil depletion, missing
 * data), so the farmer sees them without unfolding each step of the summary. Says so when the plan has none.
 */
function AgronomicRisks({ cycles }: { cycles: StrategyCycle[] }) {
  const crops = agronomicRisks(cycles)
  const fold = useFold()
  return <section className="balance-panel balance-panel-wide strategy-agronomic-risks" aria-labelledby="agronomic-risks-title">
    <h3 id="agronomic-risks-title"><FoldButton open={fold.open} onToggle={fold.toggle}>Agronomic risks</FoldButton></h3>
    {!fold.open ? null : !crops.length ? <p className="strategy-note">No agronomic risks or warnings for these crops.</p>
      : <ul className="strategy-risk-crops">{crops.map(crop => <li key={crop.key}>
          <h4><strong>{crop.cropName}</strong> <span className="strategy-table-sub">{`${whenText(crop.plantingDate, crop.plantingMonth)} → ${whenText(crop.clearanceDate, crop.clearanceMonth)}`}</span></h4>
          <ul className="strategy-notes">
            {crop.risks.map(text => <li key={`risk-${text}`} className="is-risk"><span className="strategy-note-tag is-risk">Risk</span> <span>{text}</span></li>)}
            {crop.warnings.map(text => <li key={`warning-${text}`} className="is-warning"><span className="strategy-note-tag is-warning">Warning</span> <span>{text}</span></li>)}
          </ul>
        </li>)}</ul>}
  </section>
}

/** Full-screen comparison of one strategy with the current plan, crop by crop. */
export function StrategyDetails({ name, result, baseline, range, horizon, fieldId, areaHectares, onClose, actions, notice, estimated = false }: {
  name: string; result: StrategyResult; baseline: StrategyResult | null; range: TimelineRange; fieldId: string; areaHectares: number; onClose: () => void; actions?: React.ReactNode
  /** The current plan audited by the API because the solver left it unscored: its figures are estimates. */
  estimated?: boolean
  /** The planning horizon (YYYY-MM): the water chart shows rain for every month of it, bare months included. */
  horizon?: WaterHorizon
  /** Shown under the header, e.g. the confirmation before the strategy replaces the field's plan. */
  notice?: React.ReactNode
}) {
  const schedule = useFold()
  const [today] = useState(() => new Date().toISOString().slice(0, 10))
  const panel = useRef<HTMLElement>(null)
  // Return focus to whatever opened the details (a card's Details button) when they close.
  useEffect(() => {
    const opener = document.activeElement as HTMLElement | null
    return () => { requestAnimationFrame(() => opener?.isConnected && opener.focus()) }
  }, [])

  /** Keeps Tab inside the overlay; the view behind it stays out of reach. */
  function trapTab(event: KeyboardEvent) {
    if (event.key !== 'Tab' || !panel.current) return
    event.stopPropagation()
    const focusable = [...panel.current.querySelectorAll<HTMLElement>('button:not(:disabled), [href], summary')].filter(element => element.offsetParent !== null)
    const first = focusable[0], last = focusable.at(-1)
    if (!panel.current.contains(document.activeElement)) { event.preventDefault(); first?.focus() }
    else if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus() }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus() }
  }

  return <section ref={panel} className="strategy-details" role="dialog" aria-modal="true" aria-labelledby="strategy-details-title" onKeyDown={trapTab}>
    <header className="strategy-view-head">
      <h2 id="strategy-details-title">{baseline ? `${name} vs your current plan` : name}</h2>
      <div className="strategy-view-actions">{actions}
        <button type="button" className="button button-quiet button-icon" aria-label="Close details" autoFocus onClick={onClose}><X size={16} /></button>
      </div>
    </header>
    {notice}
    {estimated && <p className="strategy-issue" role="note"><span>{ESTIMATED_PLAN_TEXT}</span></p>}
    <section className="strategy-schedule" aria-labelledby="strategy-schedule-title">
      <h3 id="strategy-schedule-title" className="strategy-schedule-title"><FoldButton open={schedule.open} onToggle={schedule.toggle}>Crop schedule</FoldButton></h3>
      {schedule.open &&
        <StrategyGantt label={`${name} crop schedule${baseline ? ' compared with your current plan' : ''}`} today={today}
          firstYear={Number(range.start.slice(0, 4))} lastYear={Number(range.end.slice(0, 4))} groups={[
            { key: 'strategy', label: name, rows: result.cycles.map(cycleRow) },
            ...(baseline ? [{ key: 'baseline', label: 'Your current plan', rows: baseline.cycles.map(cycleRow), muted: true }] : []),
          ]} />}
    </section>
    {/* The water and N-P-K charts first, so they're in view without scrolling past the long step-by-step plan below. */}
    {result.cycles.length > 0 && <div className="balance-grid">
      <WaterBalanceChart fieldId={fieldId} cycles={result.cycles} areaHectares={areaHectares} horizon={horizon} />
      <NutrientChart cycles={result.cycles} wide />
      <AgronomicRisks cycles={result.cycles} />
    </div>}
    <StrategySummary result={result} areaHectares={areaHectares} />
    <p className="strategy-disclaimer">These are suggestions, not instructions. Check the risks with your agronomist before you change the plan.</p>
  </section>
}
