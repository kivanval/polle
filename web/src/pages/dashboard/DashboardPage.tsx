import { useState } from 'react'
import { Link } from 'react-router'
import { useQueries } from '@tanstack/react-query'
import { ArrowRight, ArrowUpRight, CalendarClock, CloudSun, Plus, Sprout, Wallet } from 'lucide-react'
import { useAuth } from '@/features/auth/useAuth'
import { farmKeys, problemMessage, topLevelFields, useFarms, type Farm, type Field } from '@/features/farms/api'
import { formatHectares } from '@/features/farms/geo'
import { cropName, plantingApi, useAddPlanningYear, useCrops, usePlanningYears, useRemovePlanningYear, type Crop, type Planting } from '@/features/farms/plantings'
import { apiFetch } from '@/shared/api/client'
import { TractorLoader, TractorSpinner } from '@/shared/ui/Tractor'

export function DashboardPage() {
  const { data: user } = useAuth()
  const name = user?.displayName || user?.email.split('@')[0] || 'farmer'
  const farmQuery = useFarms()
  const farms = farmQuery.data ?? []
  const fieldLists = useQueries({ queries: farms.map(farm => ({ queryKey: farmKeys.fields(farm.id), queryFn: () => apiFetch<Field[]>(`/farms/${farm.id}/fields`) })) })
  const plantingLists = useQueries({ queries: farms.map(farm => ({ queryKey: farmKeys.plantings(farm.id), queryFn: () => plantingApi.listFarm(farm.id) })) })
  const crops = useCrops()
  const fields = fieldLists.flatMap(query => query.data ?? [])
  const plantings = plantingLists.flatMap(query => query.data ?? [])
  const firstFarm = farms[0]
  const error = farmQuery.error ?? fieldLists.find(query => query.error)?.error
  if (error) return <div className="atlas-state"><h1>Your overview didn’t load.</h1><p role="alert">{problemMessage(error)}</p>
    <button className="button button-dark" onClick={() => { void farmQuery.refetch(); fieldLists.forEach(query => { void query.refetch() }) }}>Try again</button></div>
  if (farmQuery.isPending || fieldLists.some(query => query.isPending)) return <div className="atlas-state" role="status"><TractorLoader /><p>Loading your overview…</p></div>

  // Plantings and the crop catalog enrich the overview; if they fail, land figures still show and crops say so.
  const cropsReady = !plantingLists.some(query => query.isPending) && !crops.isPending
  const cropsFailed = plantingLists.some(query => query.error) || Boolean(crops.error)
  const land = landSummary(fields, plantings)
  const growing = [...land.byCrop].sort((a, b) => b[1] - a[1])

  return <>
    <div className="workspace-heading"><span className="mono section-label">WELCOME, {name}</span>
      <h1>{fields.length ? 'Your land at a glance.' : 'A fresh start for your land.'}</h1>
      <p>{fields.length ? 'Mapped land, what is growing on it now and what comes next, across all your farms.' : 'Your workspace is ready. Let’s grow it from here.'}</p></div>

    <dl className="workspace-stats dashboard-stats">
      <div><dt>Farms</dt><dd>{farms.length} <span>{farms.length ? 'On the map' : 'No farms yet'}</span></dd></div>
      <div><dt>Mapped area</dt><dd>{land.fields ? formatHectares(land.area) : '—'} <span>{land.fields ? `${land.fields} ${land.fields === 1 ? 'field' : 'fields'}${land.subfields ? ` · ${land.subfields} ${land.subfields === 1 ? 'subfield' : 'subfields'}` : ''}` : 'A blank canvas'}</span></dd></div>
      <div><dt>Planted now</dt><dd>{!cropsReady ? '…' : cropsFailed ? '—' : land.fields ? formatHectares(land.planted) : '—'}
        <span>{cropsFailed ? 'Crop history didn’t load' : land.area ? `${Math.round((land.planted / land.area) * 100)}% of mapped land · ${land.activePlantings} active ${land.activePlantings === 1 ? 'planting' : 'plantings'}` : 'Map a field to begin'}</span></dd></div>
      <div><dt>Crops growing</dt><dd>{!cropsReady ? '…' : cropsFailed ? '—' : growing.length}
        <span>{growing.length ? growing.slice(0, 3).map(([crop]) => cropName(crops.data ?? [], crop)).join(', ') : land.planned ? `${land.planned} planned` : 'Nothing growing now'}</span></dd></div>
    </dl>

    {land.fields > 0 && cropsReady && !cropsFailed
      ? <div className="dashboard-overview">
          <CropComposition area={land.area} planted={land.planted} growing={growing} crops={crops.data ?? []} />
          <ComingUp plantings={plantings} fields={fields} farms={farms} crops={crops.data ?? []} />
        </div>
      : <section className="land-empty"><div className="contour-art"><img src="/images/contours.webp" alt="Aerial photograph of contour strip farming near Bošovice, Czech Republic" /><span className="contour-label mono">YOUR NEXT CHAPTER, UNMAPPED.</span></div><div className="land-empty-copy"><span className="mono section-label">YOUR NEXT CHAPTER</span>{firstFarm ? <><h2>Keep mapping your land.</h2><p>Open {firstFarm.name} to outline fields, rename them and adjust their boundaries.</p><Link className="button button-dark" to={`/dashboard/farms/${firstFarm.id}`}>Open {firstFarm.name} <ArrowRight size={15} /></Link><Link className="subtle-link" to="/dashboard/farms">All farms <ArrowRight size={15} /></Link></>
          : <><h2>Your land intelligence starts here.</h2><p>Create your first farm: give it a name, mark it on the map, then outline every field around it.</p><Link className="button button-dark" to="/dashboard/farms/new"><Plus size={15} />Create farm</Link></>}</div></section>}

    <div className="dashboard-next"><Link className="future-feature" to="/dashboard/resources"><Wallet size={39} strokeWidth={1} /><div><span className="mono section-label">RESOURCES & FINANCE</span><h2>Budgets and machinery, together.</h2><p>See every farm’s budget, how it is planned and the machinery it runs, in one view.</p></div><ArrowUpRight size={20} /></Link><Link className="future-feature" to="/dashboard/climate"><CloudSun size={39} strokeWidth={1} /><div><span className="mono section-label">CLIMATE</span><h2>Drought, ten years ahead.</h2><p>A worldwide monthly map of drought, temperature and rain from the last decade’s trend, carried ten years forward.</p></div><ArrowUpRight size={20} /></Link><Link className="future-feature" to="/dashboard/farms/planning"><Sprout size={39} strokeWidth={1} /><div><span className="mono section-label">PLANNING</span><h2>Think seasons ahead.</h2><p>Every field’s crop plan on one timeline, with the plans the solver suggests to compare against your own.</p></div><ArrowUpRight size={20} /></Link></div>
  </>
}

/**
 * Land and what grows on it now. Subfields lie inside their parent and cannot grow a crop while the parent does,
 * so a parent counts as planted when it has a current crop, else through its planted subfields; nothing is counted twice.
 */
function landSummary(fields: Field[], plantings: Planting[]) {
  const top = topLevelFields(fields)
  const current = new Map(plantings.filter(planting => planting.status === 'current').map(planting => [planting.fieldId, planting.cropId]))
  const byCrop = new Map<number, number>()
  let planted = 0
  for (const parent of top) {
    const crop = current.get(parent.id)
    const parts = crop ? [{ area: parent.areaHectares, crop }]
      : fields.filter(child => child.parentFieldId === parent.id && current.has(child.id)).map(child => ({ area: child.areaHectares, crop: current.get(child.id)! }))
    for (const part of parts) { planted += part.area; byCrop.set(part.crop, (byCrop.get(part.crop) ?? 0) + part.area) }
  }
  return {
    fields: top.length, subfields: fields.length - top.length, area: top.reduce((sum, field) => sum + field.areaHectares, 0), planted, byCrop,
    activePlantings: current.size, planned: plantings.filter(planting => planting.status === 'planned').length,
  }
}

const cropColors = ['#5f8f3e', '#d08c38', '#4f7cac', '#a7c957', '#7a6248', '#6fb3c8', '#386641', '#c46b5a']

function CropComposition({ area, planted, growing, crops }: { area: number; planted: number; growing: [number, number][]; crops: Crop[] }) {
  const shown = growing.slice(0, 6)
  const rest = growing.slice(6).reduce((sum, [, value]) => sum + value, 0)
  const segments = [...shown.map(([crop, value], index) => ({ label: cropName(crops, crop), value, color: cropColors[index] })),
    ...(rest > 0 ? [{ label: 'Other crops', value: rest, color: '#9aa39b' }] : [])]
  return <section className="dashboard-card" aria-labelledby="crop-composition">
    <header><h2 id="crop-composition">What’s growing now</h2><p>Share of your mapped land under each current crop.</p></header>
    {segments.length === 0
      ? <p className="dashboard-empty"><Sprout size={18} />No crop is growing on any field today. Record plantings from a field’s crop history.</p>
      : <>
          <div className="crop-bar" role="img" aria-label={`${segments.map(item => `${item.label} ${formatHectares(item.value)}`).join(', ')}; ${formatHectares(Math.max(0, area - planted))} unplanted`}>
            {segments.map(item => <span key={item.label} style={{ width: `${(item.value / area) * 100}%`, background: item.color }} title={`${item.label}: ${formatHectares(item.value)}`} />)}
          </div>
          <ul className="crop-legend">
            {segments.map(item => <li key={item.label}><i style={{ background: item.color }} />{item.label}<span className="mono">{formatHectares(item.value)} · {Math.round((item.value / area) * 100)}%</span></li>)}
            <li className="is-rest"><i />Not planted now<span className="mono">{formatHectares(Math.max(0, area - planted))}</span></li>
          </ul>
        </>}
  </section>
}

const day = (iso: string) => new Date(`${iso}T00:00:00Z`)
const shortDate = (iso: string) => day(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', timeZone: 'UTC' })

const MAX_YEARS_AHEAD = 20
const SHOWN_EVENTS = 8

/**
 * Upcoming planting starts and harvests across all farms, one calendar year at a time, soonest first. The tabs list this year,
 * every later year with a planting or harvest date, and the years the user added to plan ahead (stored per user on the server).
 */
function ComingUp({ plantings, fields, farms, crops }: { plantings: Planting[]; fields: Field[]; farms: Farm[]; crops: Crop[] }) {
  const [today] = useState(() => new Date().toISOString().slice(0, 10))
  const thisYear = Number(today.slice(0, 4))
  const added = usePlanningYears()
  const addYear = useAddPlanningYear()
  const removeYear = useRemovePlanningYear()
  const [picked, setPicked] = useState(thisYear)

  const events = [
    ...plantings.filter(planting => planting.status === 'planned').map(planting => ({ planting, date: planting.plantingDate, kind: 'Planting' as const })),
    ...plantings.filter(planting => planting.status !== 'historical' && planting.harvestingDate && planting.harvestingDate >= today)
      .map(planting => ({ planting, date: planting.harvestingDate!, kind: 'Harvest' as const })),
  ].sort((a, b) => a.date.localeCompare(b.date))
  const eventYear = (date: string) => Number(date.slice(0, 4))
  const addedYears = (added.data ?? []).filter(year => year >= thisYear)
  const years = [...new Set([thisYear, ...events.map(event => eventYear(event.date)), ...addedYears])].sort((a, b) => a - b)
  const year = years.includes(picked) ? picked : thisYear
  const yearEvents = events.filter(event => eventYear(event.date) === year)
  const shown = yearEvents.slice(0, SHOWN_EVENTS)
  const nextYear = years.at(-1)! + 1
  const canAdd = nextYear <= thisYear + MAX_YEARS_AHEAD
  const removable = addedYears.includes(year) && yearEvents.length === 0 && year !== thisYear
  const busy = addYear.isPending || removeYear.isPending
  const mutationError = addYear.error ?? removeYear.error

  async function add() {
    removeYear.reset()
    try { await addYear.mutateAsync(nextYear); setPicked(nextYear) } catch { /* addYear.error is shown below the tabs */ }
  }
  async function remove() {
    addYear.reset()
    try { await removeYear.mutateAsync(year); setPicked(thisYear) } catch { /* removeYear.error is shown below the tabs */ }
  }

  return <section className="dashboard-card" aria-labelledby="coming-up">
    <header><h2 id="coming-up">Coming up</h2><p>Plantings and harvests from your crop history, year by year. Add a year to plan further ahead.</p></header>
    <div className="coming-years">
      <div role="tablist" aria-label="Planning year" className="coming-year-tabs">
        {years.map(value => <button key={value} type="button" role="tab" id={`coming-year-${value}`} aria-selected={value === year} aria-controls="coming-year-panel"
          onClick={() => setPicked(value)}>{value}</button>)}
      </div>
      {canAdd && <button type="button" className="coming-year-add" disabled={busy || added.isPending} onClick={() => void add()}>
        {addYear.isPending ? <TractorSpinner size={13} /> : <Plus size={13} />}Add {nextYear}</button>}
    </div>
    {added.error && <p className="field-error" role="alert">Your added planning years didn’t load. {problemMessage(added.error)}</p>}
    {mutationError && <p className="field-error" role="alert">{problemMessage(mutationError)}</p>}
    <div role="tabpanel" id="coming-year-panel" aria-labelledby={`coming-year-${year}`}>
    {shown.length === 0
      ? <div className="dashboard-empty coming-empty"><CalendarClock size={18} />
          <p>{year === thisYear ? 'No planned plantings or harvest dates for the rest of this year.' : `Nothing planned for ${year} yet. Add crops for ${year} from a field’s crop history on its farm map.`}
            {removable && <> <button type="button" className="text-button" disabled={busy} onClick={() => void remove()}>Remove {year} from the plan</button></>}</p>
        </div>
      : <ol className="coming-up">{shown.map(({ planting, date, kind }) => {
          const field = fields.find(item => item.id === planting.fieldId)
          const farm = farms.find(item => item.id === field?.farmId)
          const days = Math.round((day(date).getTime() - day(today).getTime()) / 86_400_000)
          return <li key={`${planting.id}-${kind}`}>
            <span className="coming-date mono">{shortDate(date)}<small>{days === 0 ? 'today' : `in ${days} d`}</small></span>
            <span><strong>{kind}: {cropName(crops, planting.cropId)}</strong>
              {field && farm && <Link to={`/dashboard/farms/${farm.id}`} className="coming-place">{field.name} · {farm.name}</Link>}</span>
          </li>
        })}</ol>}
    {yearEvents.length > shown.length && <p className="coming-more">and {yearEvents.length - shown.length} more in {year}, on each field’s crop history.</p>}
    </div>
  </section>
}
