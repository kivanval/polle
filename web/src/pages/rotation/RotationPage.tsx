import { Link, useSearchParams } from 'react-router'
import { useQueries } from '@tanstack/react-query'
import { AlertTriangle, ArrowUpRight, Bookmark, CornerDownRight, LayoutList, Plus } from 'lucide-react'
import { farmKeys, nestFields, problemMessage, topLevelFields, useFarms, type Farm, type Field } from '@/features/farms/api'
import { FieldTimeline } from '@/features/farms/FieldTimeline'
import { formatHectares } from '@/features/farms/geo'
import { cropName, plantingApi, useCrops, type Crop, type Planting } from '@/features/farms/plantings'
import { timelineRange } from '@/features/farms/timeline'
import { FieldGlyph } from '@/features/map/FieldGlyph'
import { fieldPlan, planBars, rotationSummary } from '@/features/rotation/rotation'
import { strategyKeys, type SavedStrategy } from '@/features/strategies/api'
import { monthText } from '@/features/strategies/format'
import { PlanStrategiesView } from '@/features/strategies/PlanStrategiesView'
import { usePlanParam } from '@/features/strategies/planParam'
import { apiFetch } from '@/shared/api/client'
import { TractorLoader } from '@/shared/ui/Tractor'

const monthYear = new Intl.DateTimeFormat('en', { month: 'short', year: 'numeric', timeZone: 'UTC' })
const formatMonth = (iso: string) => monthYear.format(new Date(`${iso}T00:00:00Z`))
const todayIso = () => new Date().toISOString().slice(0, 10)
const plural = (count: number, one: string, many = `${one}s`) => `${count} ${count === 1 ? one : many}`

/**
 * Planning (formerly Crop rotation), the third view of the farms workspace: each field's crop plan on one time axis per
 * farm, and the same "Plan & strategies"
 * view as on the farm map, where the farmer edits the crops and compares the solver's rotation strategies.
 */
export function RotationPage() {
  const farmQuery = useFarms()
  const farms = farmQuery.data ?? []
  const fieldLists = useQueries({ queries: farms.map(farm => ({ queryKey: farmKeys.fields(farm.id), queryFn: () => apiFetch<Field[]>(`/farms/${farm.id}/fields`) })) })
  const plantingLists = useQueries({ queries: farms.map(farm => ({ queryKey: farmKeys.plantings(farm.id), queryFn: () => plantingApi.listFarm(farm.id) })) })
  const crops = useCrops()
  const [params, setParams] = useSearchParams()
  const [planning, setPlanning, openStrategy] = usePlanParam()
  const fields = fieldLists.flatMap(item => item.data ?? [])
  // Same cache key as the plan view's own list, so a strategy saved there shows up here at once.
  const savedLists = useQueries({ queries: fields.map(field => ({ queryKey: strategyKeys.saved(field.id),
    queryFn: () => apiFetch<SavedStrategy[]>(`/fields/${field.id}/strategies`) })) })
  const savedByField = new Map(fields.map((field, index) => [field.id, savedLists[index]?.data ?? []]))

  const lists = [...fieldLists, ...plantingLists]
  const error = farmQuery.error ?? lists.find(item => item.error)?.error
  if (error) return <div className="atlas-state"><h1>Your crop plans didn’t load.</h1><p role="alert">{problemMessage(error)}</p>
    <button className="button button-dark" onClick={() => { void farmQuery.refetch(); lists.forEach(item => { void item.refetch() }) }}>Try again</button></div>
  if (farmQuery.isPending || lists.some(item => item.isPending)) return <div className="atlas-state" role="status"><TractorLoader /><p>Laying out your seasons…</p></div>

  if (farms.length === 0) return <>
    <Heading summary={null} />
    <section className="farms-empty"><img src="/images/contours.webp" alt="" />
      <div><h2>No farms yet.</h2><p>Rotation plans belong to fields. Create a farm, outline its fields, then plan what each one grows.</p>
        <Link className="button button-dark" to="/dashboard/farms/new"><Plus size={17} />Create farm</Link></div></section>
  </>

  const plantings = plantingLists.flatMap(item => item.data ?? [])
  const selectedId = params.get('farm')
  const selected = farms.find(farm => farm.id === selectedId) ?? null
  const shown = selected ? [selected] : farms
  const shownFields = fields.filter(field => shown.some(farm => farm.id === field.farmId))
  const summary = rotationSummary(shownFields, plantings)
  const catalog = crops.data ?? []
  const planField = planning ? fields.find(field => field.id === planning) ?? null : null
  const selectFarm = (farmId: string | null) => setParams(current => {
    const next = new URLSearchParams(current)
    if (farmId) next.set('farm', farmId); else next.delete('farm')
    return next
  }, { replace: true })

  return <div className="rotation-page">
    <Heading summary={summary} />
    <div className="finance-filter" role="group" aria-label="Show farms">
      <button type="button" aria-pressed={!selected} onClick={() => selectFarm(null)}>All farms <span className="mono">{farms.length}</span></button>
      {farms.map(farm => <button key={farm.id} type="button" aria-pressed={selected?.id === farm.id} onClick={() => selectFarm(farm.id)}>{farm.name}</button>)}
    </div>
    <dl className="finance-kpis rotation-kpis">
      <div><dt>Fields with a plan</dt><dd>{summary.withPlan}<small> / {summary.fields}</small></dd><span>A crop growing now or planned ahead.</span></div>
      <div><dt>Crops planned ahead</dt><dd>{summary.upcoming}</dd><span>Sowings still to come, on every field shown.</span></div>
      <div className={summary.withoutPlan ? 'is-warn' : ''}><dt>Fields without a plan</dt><dd>{summary.withoutPlan}</dd><span>Nothing growing and nothing planned yet.</span></div>
      <div className={summary.noSoil ? 'is-warn' : ''}><dt>No soil data</dt><dd>{summary.noSoil}</dd><span>Outside mapped land, so the solver can’t model them.</span></div>
    </dl>
    {crops.error && <p className="field-error" role="alert">The crop catalog didn’t load, so plans can’t be opened. {problemMessage(crops.error)}</p>}
    {shown.map(farm => <FarmRotation key={farm.id} farm={farm} fields={fields.filter(field => field.farmId === farm.id)}
      plantings={plantings} saved={savedByField} crops={catalog} cropsReady={catalog.length > 0} onPlan={setPlanning} />)}
    {planField && <PlanStrategiesView field={planField} plantings={plantings.filter(planting => planting.fieldId === planField.id)} crops={catalog}
      openSavedId={openStrategy} onSavedOpened={() => setPlanning(planField.id)} onClose={() => setPlanning(null)} />}
  </div>
}

function Heading({ summary }: { summary: ReturnType<typeof rotationSummary> | null }) {
  return <div className="workspace-heading"><span className="mono section-label">PLANNING</span><h1>Every field’s seasons, side by side.</h1>
    <p>{summary && summary.fields
      ? `${plural(summary.withPlan, 'field')} of ${summary.fields} ${summary.withPlan === 1 ? 'has' : 'have'} a plan. Open a field to edit its crops, ask the solver for rotation strategies, and compare them with your own plan.`
      : 'Each field’s crop plan on one timeline, with rotation strategies from the solver to compare against your own.'}</p></div>
}

const SAVED_SHOWN = 3

function FarmRotation({ farm, fields, plantings, saved, crops, cropsReady, onPlan }: {
  farm: Farm; fields: Field[]; plantings: Planting[]; saved: Map<string, SavedStrategy[]>; crops: Crop[]; cropsReady: boolean
  onPlan: (fieldId: string, strategyId?: string) => void
}) {
  const ids = new Set(fields.map(field => field.id))
  const own = plantings.filter(planting => ids.has(planting.fieldId))
  const today = todayIso()
  // One axis per farm, so the fields' seasons line up and can be compared at a glance.
  const range = timelineRange(own, today)
  const top = topLevelFields(fields)
  return <section className="finance-card rotation-farm" aria-labelledby={`rotation-${farm.id}`}>
    <header className="fields-farm-head">
      <div><h2 id={`rotation-${farm.id}`}>{farm.name}</h2>
        <p>{plural(top.length, 'field')} · {formatHectares(top.reduce((sum, field) => sum + field.areaHectares, 0))}{farm.location ? ` · ${farm.location}` : ''}</p></div>
      <Link className="subtle-link" to={`/dashboard/farms/${farm.id}`}>Open farm map <ArrowUpRight size={13} /></Link>
    </header>
    {fields.length === 0
      ? <p className="dashboard-empty">No fields on this farm yet. <Link className="text-button" to={`/dashboard/farms/${farm.id}`}>Draw the first one</Link></p>
      : <>
        <div className="rotation-axis" aria-hidden="true"><span /><span /><FieldTimeline label="Years" range={range} lanes={[]} /><span /></div>
        <ul className="rotation-rows">{nestFields(fields).map(field => {
          const fieldPlantings = own.filter(planting => planting.fieldId === field.id)
          const plan = fieldPlan(fieldPlantings)
          const strategies = saved.get(field.id) ?? []
          return <li key={field.id} className={`rotation-row ${field.parentFieldId ? 'is-subfield' : ''}`}>
            <FieldGlyph boundary={field.boundary} />
            <div className="rotation-field">
              <span className="rotation-field-name">
                {field.parentFieldId && <CornerDownRight size={13} className="field-row-branch" aria-hidden="true" />}
                <span title={field.name}>{field.name}</span><span className="mono">{formatHectares(field.areaHectares)}</span>
              </span>
              <span className="rotation-field-meta">
                {plan.growing && <span className="is-growing">Growing: {cropName(crops, plan.growing.cropId)}</span>}
                {plan.next && <span>Next: {cropName(crops, plan.next.cropId)} · {formatMonth(plan.next.plantingDate)}</span>}
                {!plan.growing && !plan.next && <span className="is-empty">No crops planned</span>}
                {plan.ahead > 1 && <span className="rotation-chip mono">{plan.ahead} planned</span>}
                {!field.soilAvailable && <span className="rotation-chip is-warn" title="This field lies outside mapped land, so the solver can’t model it.">
                  <AlertTriangle size={11} aria-hidden="true" />No soil data</span>}
              </span>
              {strategies.length > 0 && <ul className="rotation-saved" aria-label={`Saved strategies for ${field.name}`}>
                {strategies.slice(0, SAVED_SHOWN).map(item => <li key={item.id}>
                  <button type="button" className="rotation-saved-link" disabled={!cropsReady} onClick={() => onPlan(field.id, item.id)}
                    title={`Open ${item.name}, ${monthText(item.startMonth)} – ${monthText(item.endMonth)}`}>
                    <Bookmark size={11} aria-hidden="true" /><span>{item.name}</span><span className="mono">{`${item.startMonth.slice(0, 4)}–${item.endMonth.slice(0, 4)}`}</span>
                  </button></li>)}
                {strategies.length > SAVED_SHOWN && <li><button type="button" className="rotation-saved-link is-more" disabled={!cropsReady} onClick={() => onPlan(field.id)}>
                  <span>{`+${strategies.length - SAVED_SHOWN} more`}</span></button></li>}
              </ul>}
            </div>
            <FieldTimeline compact label={`Crop plan of ${field.name}`} range={range} lanes={[{ key: 'plan', bars: planBars(fieldPlantings, crops) }]} />
            <button type="button" className="button button-dark rotation-plan" disabled={!cropsReady} onClick={() => onPlan(field.id)}
              aria-label={`Plan and strategies for ${field.name}`}><LayoutList size={15} />Plan</button>
          </li>
        })}</ul>
      </>}
  </section>
}
