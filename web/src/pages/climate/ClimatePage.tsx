import { useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { CircleMarker, useMapEvents } from 'react-leaflet'
import { ArrowUpRight, ChevronLeft, ChevronRight, CloudRain, Droplets, MapPin, Sun, Thermometer } from 'lucide-react'
import { ApiError } from '@/shared/api/client'
import { BaseMap } from '@/features/map/BaseMap'
import { currentMonth, defaultMonth, layerSource, mapLayers, satelliteTime, useDroughtOverview, useDroughtPoint, type DroughtCell, type DroughtClass, type DroughtOverview, type DroughtPoint, type GridLayer, type MapLayer } from '@/features/drought/api'
import { celsius, cellVerdict, formatMonth, kindLabels, layerDateNote, mm, percentileText } from '@/features/drought/format'
import { LayerChooser, LayerLegend, MapDataLayer } from '@/features/drought/MapDataLayer'
import { gibsText, useLayerPoint } from '@/features/drought/layerPoint'
import { percentileBands, scalePosition, SCALES, yearMonths } from '@/features/drought/reading'
import { TractorLoader, TractorSpinner } from '@/shared/ui/Tractor'
import { ClimateFocusHead } from './ClimateFocusHead'
import { gridClass, gridText, gridValue, readingBadge } from './focus'

export function ClimatePage() {
  const overview = useDroughtOverview()
  if (overview.isPending) return <div className="atlas-state"><TractorLoader /><p>Loading the climate map…</p></div>
  if (overview.error) {
    const notReady = overview.error instanceof ApiError && overview.error.status === 503
    return <div className="atlas-state"><h1>{notReady ? 'The climate map isn’t ready yet.' : 'The climate map didn’t load.'}</h1>
      <p>{notReady ? 'The climate trend model has not been built for this server yet.' : overview.error.message}</p>
      <button className="button button-dark" onClick={() => void overview.refetch()}>Try again</button></div>
  }
  if (overview.data.months.length === 0) return <div className="atlas-state"><h1>No months in the climate model.</h1>
    <p>Rebuild the trend model to add them.</p></div>
  return <DroughtAtlas overview={overview.data} />
}

/** Below this width the focus panel stacks under the map, so a pick scrolls it into view. */
const STACKED = '(max-width: 1099px)'

/**
 * The world climate map in three parts: controls on the left, the map, and a focus panel on the right that shows the
 * picked place at once. Narrow screens stack them map, focus panel, controls.
 */
function DroughtAtlas({ overview }: { overview: DroughtOverview }) {
  const [month, setMonth] = useState(() => defaultMonth(overview))
  const [layerId, setLayerId] = useState('drought')
  const [picked, setPicked] = useState<{ lat: number; lng: number } | null>(null)
  // Folds the card down to its title so the map shows; the place and its marker stay picked.
  const [collapsed, setCollapsed] = useState(false)
  const focus = useRef<HTMLElement>(null)
  const { months } = overview
  const position = months.findIndex(item => item.month === month)
  const kind = months[position].kind
  const point = useDroughtPoint(picked)
  const layers = mapLayers(overview)
  const layer = layers.find(item => item.id === layerId) ?? layers[0]
  const dateNote = layerDateNote(layer, month)
  const folded = collapsed && picked !== null
  const badge = readingBadge(layer, point, month, overview.classes, overview.noDrought?.color ?? null)
  const now = currentMonth()
  const years = [...new Set(months.map(item => item.month.slice(0, 4)))]
  const [first, last] = overview.fitYears
  const step = (offset: number) => {
    const next = months[position + offset]
    if (next) setMonth(next.month)
  }
  // A month not on the timeline (a year that starts or ends part-way) stays on the nearest one there is.
  const goTo = (target: string) => setMonth(months.some(item => item.month === target) ? target
    : months.reduce((best, item) => Math.abs(monthIndex(item.month) - monthIndex(target)) < Math.abs(monthIndex(best) - monthIndex(target)) ? item.month : best, months[0].month))
  function pick(place: { lat: number; lng: number }) {
    setPicked(place)
    setCollapsed(false)
    if (!window.matchMedia?.(STACKED).matches) return
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    requestAnimationFrame(() => focus.current?.scrollIntoView({ behavior: reduced ? 'auto' : 'smooth', block: 'nearest' }))
  }

  return <div className="farm-atlas drought-atlas">
    <aside className="atlas-panel climate-rail" aria-label="Climate map controls">
      <div className="atlas-panel-head">
        <span className="mono section-label">CLIMATE INTELLIGENCE / LAYERS</span>
        <h1>Drought and weather, worldwide.</h1>
        <p className="atlas-location">Monthly drought, temperature and rain from the {first}–{last} trend, carried ten years ahead, plus NASA satellite views of vegetation and soil. Click the map to read one place.</p>
      </div>

      <section className="drought-section climate-month" aria-label="Month">
        <div className="drought-day">
          <button type="button" className="drought-step" aria-label="Previous month" disabled={position <= 0} onClick={() => step(-1)}><ChevronLeft size={16} /></button>
          <div className="drought-day-label">
            <strong>{formatMonth(month)}</strong>
            <span className={`drought-kind is-${kind}`}>{month === now ? 'This month · ' : ''}{kindLabels[kind]}</span>
          </div>
          <button type="button" className="drought-step" aria-label="Next month" disabled={position >= months.length - 1} onClick={() => step(1)}><ChevronRight size={16} /></button>
        </div>
        <label className="drought-range">
          <span className="sr-only">Month on the map</span>
          <input type="range" min={0} max={months.length - 1} value={position}
            aria-valuetext={`${formatMonth(month)}, ${kindLabels[kind]}`}
            onChange={event => setMonth(months[Number(event.target.value)].month)} />
        </label>
        <div className="drought-range-ends mono"><span>{formatMonth(months[0].month)}</span>
          {month !== now && months.some(item => item.month === now)
            && <button type="button" className="text-link" onClick={() => setMonth(now)}>Back to this month</button>}
          <span>{formatMonth(months[months.length - 1].month)}</span></div>
        <label className="drought-jump"><span>Go to year</span>
          <select value={month.slice(0, 4)} onChange={event => goTo(`${event.target.value}-${month.slice(5)}`)}>
            {years.map(year => <option key={year} value={year}>{year}</option>)}
          </select></label>
        <p className="drought-note">{overview.kinds[kind]}.</p>
      </section>

      <section className="drought-section" aria-label="Layers">
        <h2 className="drought-heading">Layer</h2>
        <LayerChooser overview={overview} layers={layers} value={layer.id} onChange={setLayerId} name="climate-layer" />
      </section>

      <section className="drought-section" aria-label="Legend">
        <h2 className="drought-heading">{layer.label}</h2>
        <p className="drought-note layer-description">{layer.description}</p>
        <p className="drought-note layer-source">Source: {layerSource(layer, overview).provider}. Coverage: {layerSource(layer, overview).coverage}.</p>
        {dateNote && <p className="drought-note is-held">{dateNote}</p>}
        {layer.id !== 'drought' ? <LayerLegend layer={layer} /> : <ul className="drought-legend">{[...overview.classes].reverse().map(item => <li key={item.code}>
          <span className="drought-swatch" style={{ background: item.color }} /><span>{item.label}</span>
          <span className="mono">{item.code} · ≤{item.percentile_max}%</span></li>)}
          {overview.noDrought && <li><span className="drought-swatch" style={{ background: overview.noDrought.color }} /><span>{overview.noDrought.label}</span><span className="mono">&gt;30%</span></li>}
          <li><span className="drought-swatch is-empty" /><span>Not rated</span><span className="mono">desert, dry season, ice</span></li></ul>}
      </section>

      <footer className="atlas-panel-foot drought-foot">
        <p>{overview.method}</p>
        <p>Drought base period {overview.index.base_period}. Satellite layers show real images, not the trend.</p>
        <ul>{overview.sources.map(source => <li key={source.url}><a className="text-link" href={source.url} target="_blank" rel="noreferrer">{source.name} <ArrowUpRight size={12} /></a></li>)}</ul>
      </footer>
    </aside>

    <BaseMap className="drought-map" label="World climate map" center={[30, 15]} zoom={2}>
      <MapDataLayer overview={overview} layer={layer} month={month} opacity={layer.kind === 'grid' ? 0.72 : 0.85} />
      <PickPoint onPick={pick} />
      {picked && <CircleMarker center={picked} radius={7} pathOptions={{ color: '#142b22', weight: 2, fillColor: '#d6ed83', fillOpacity: 1 }} />}
    </BaseMap>

    <section ref={focus} className={`climate-focus ${picked ? 'is-picked' : ''} ${folded ? 'is-collapsed' : ''}`} aria-live="polite" aria-label="Selected place">
      <ClimateFocusHead picked={picked} collapsed={collapsed} badge={badge} onCollapse={setCollapsed}
        onClear={() => { setPicked(null); setCollapsed(false) }}
        details={<><span>{formatMonth(month)}</span><span className={`drought-kind is-${kind}`}>{kindLabels[kind]}</span><span>{layer.label}</span></>} />
      {/* Hidden, not removed, while collapsed: the reading stays loaded, and showing it again replays its entrance. */}
      <div id="climate-focus-body" className="climate-focus-body" hidden={folded} key={picked ? `${picked.lat},${picked.lng}` : 'none'}>
        {/* What the chosen layer shows at the place comes first, so switching layers changes the reading. */}
        {!picked ? <div className="climate-focus-empty"><MapPin size={28} aria-hidden="true" />
            <p>Click anywhere on land to read the chosen layer there.</p></div>
          : layer.kind !== 'grid' ? <ObservedReading layer={layer} picked={picked} month={month} point={point.data ?? null} />
          : point.isPending ? <p className="drought-note"><TractorSpinner size={14} /> Reading this place…</p>
            : point.error ? <p className="drought-note is-error">{point.error.message}</p>
              : layer.id === 'drought'
                ? <Reading point={point.data} month={month} classes={overview.classes} noDroughtColor={overview.noDrought?.color ?? null} onMonth={goTo} />
                : <GridReading layer={layer} point={point.data} month={month} onMonth={goTo} />}
      </div>
    </section>
  </div>
}

function PickPoint({ onPick }: { onPick: (point: { lat: number; lng: number }) => void }) {
  useMapEvents({ click: event => onPick({ lat: round(event.latlng.lat), lng: round(((event.latlng.lng + 540) % 360) - 180) }) })
  return null
}

/** The picked place for the chosen month: the drought verdict, then each figure as a tile with its own small scale. */
function Reading({ point, month, classes, noDroughtColor, onMonth }: {
  point: DroughtPoint; month: string; classes: DroughtClass[]; noDroughtColor: string | null; onMonth: (month: string) => void
}) {
  const cell = point.months.find(item => item.month === month)
  if (point.status !== 'complete' || !cell) return <p className="drought-note">{point.reason ?? 'No value here.'}</p>
  const place = <p className="climate-cell mono">{`${point.cellLatitude?.toFixed(2)}°, ${point.cellLongitude?.toFixed(2)}° grid cell`}</p>
  const strip = <MonthStrip point={point} month={month} onMonth={onMonth} label="Drought" verdict={item => cellVerdict(item, classes, noDroughtColor)} />
  if (cell.status !== 'ok') return <>
    {place}
    <div className="climate-verdict is-unrated"><span className="drought-swatch is-empty" />
      <p>{cell.status === 'arid' ? 'Dry season or desert: it normally barely rains here at this time of year, so the index does not rate drought.'
        : cell.status === 'polar' ? 'Ice or high-Arctic climate with no growing season, so the index does not rate drought.'
          : 'Not enough gauge data for this cell.'}</p></div>
    {cell.status !== 'no_data' && <Weather cell={cell} />}
    {strip}
  </>
  const level = cell.droughtClass >= 0 ? classes[cell.droughtClass] : null
  const bands = percentileBands(classes, noDroughtColor)
  const balance = cell.waterBalance30dMm
  return <>
    {place}
    <div className="climate-verdict" style={{ '--verdict': level?.color ?? noDroughtColor ?? 'var(--sage)' } as CSSProperties}>
      <span className="climate-verdict-swatch" aria-hidden="true" />
      <div>
        <p className="climate-verdict-label">{level ? level.label : 'No drought'}</p>
        <p className="climate-verdict-sub">Drought percentile <strong className="mono">{percentileText(cell.percentile)}</strong></p>
      </div>
    </div>
    <div className="climate-scale" role="img" aria-label={`Drought percentile ${percentileText(cell.percentile)} on a scale from the driest (0) to the 50th percentile`}>
      <div className="climate-scale-track is-bands">{bands.map(band => <span key={band.label} style={{ left: `${band.from}%`, width: `${band.to - band.from}%`, background: band.color }} />)}</div>
      {cell.percentile !== null && <span className="climate-scale-mark" style={{ left: `${scalePosition(cell.percentile, SCALES.percentile.min, SCALES.percentile.max)}%` }} />}
      <div className="climate-scale-ends mono"><span>Driest</span><span>50th+</span></div>
    </div>
    <div className="climate-kpis">
      <Kpi icon={<Droplets size={15} />} label="Water balance, 30 days" value={mm(balance)}
        tone={balance === null ? undefined : balance < 0 ? 'Deficit' : 'Surplus'} toneClass={balance !== null && balance < 0 ? 'is-dry' : 'is-wet'}>
        {balance !== null && <Bar diverging from={SCALES.balance.min} to={SCALES.balance.max} value={balance} />}
      </Kpi>
      <Kpi icon={<Sun size={15} />} label="Evaporation demand" value={mm(cell.et030dMm)}>
        {cell.et030dMm !== null && <Bar from={SCALES.evaporation.min} to={SCALES.evaporation.max} value={cell.et030dMm} />}
      </Kpi>
    </div>
    <Weather cell={cell} />
    {strip}
  </>
}

/** Temperature and rain tiles for the month. */
function Weather({ cell }: { cell: DroughtCell }) {
  const { min, max } = SCALES.temperature
  return <>
    <div className="climate-kpis">
      <Kpi icon={<Thermometer size={15} />} label="Air temperature" value={celsius(cell.tmeanC)} detail={<>Min – max <span className="mono">{celsius(cell.tminC)} – {celsius(cell.tmaxC)}</span></>}>
        {cell.tminC !== null && cell.tmaxC !== null && <div className="climate-bar is-temperature" aria-hidden="true">
          <span className="climate-bar-range" style={{ left: `${scalePosition(cell.tminC, min, max)}%`, width: `${Math.max(2, scalePosition(cell.tmaxC, min, max) - scalePosition(cell.tminC, min, max))}%` }} />
          {cell.tmeanC !== null && <span className="climate-bar-dot" style={{ left: `${scalePosition(cell.tmeanC, min, max)}%` }} />}
        </div>}
      </Kpi>
      <Kpi icon={<CloudRain size={15} />} label="Rain, 30 days" value={mm(cell.precipitation30dMm)}>
        {cell.precipitation30dMm !== null && <Bar from={SCALES.rain.min} to={SCALES.rain.max} value={cell.precipitation30dMm} />}
      </Kpi>
    </div>
  </>
}

function Kpi({ icon, label, value, detail, tone, toneClass = '', children }: {
  icon: ReactNode; label: string; value: string; detail?: ReactNode; tone?: string; toneClass?: string; children?: ReactNode
}) {
  return <div className="climate-kpi">
    <p className="climate-kpi-label"><span aria-hidden="true">{icon}</span>{label}</p>
    <p className="climate-kpi-value mono">{value}{tone && <span className={`climate-tone ${toneClass}`}>{tone}</span>}</p>
    {detail && <p className="climate-kpi-detail">{detail}</p>}
    {children}
  </div>
}

/** A value on a fixed scale; a diverging bar fills from the middle (zero) towards the value. */
function Bar({ from, to, value, diverging = false }: { from: number; to: number; value: number; diverging?: boolean }) {
  const at = scalePosition(value, from, to)
  const style = diverging ? { left: `${Math.min(50, at)}%`, width: `${Math.abs(at - 50)}%` } : { left: 0, width: `${at}%` }
  return <div className={`climate-bar ${diverging ? 'is-diverging' : ''}`} aria-hidden="true">
    <span className={`climate-bar-fill ${diverging && value < 0 ? 'is-dry' : ''}`} style={style} />
  </div>
}

/** The chosen year month by month, coloured by the layer's classes; a month opens on the map. */
function MonthStrip({ point, month, onMonth, label, verdict: verdictOf }: {
  point: DroughtPoint; month: string; onMonth: (month: string) => void; label: string
  verdict: (cell: DroughtCell) => { label: string; color: string | null }
}) {
  const cells = yearMonths(point, month)
  if (cells.length < 2) return null
  return <div className="climate-strip">
    <h3>{label}, {month.slice(0, 4)} month by month</h3>
    <div role="group" aria-label={`${label} in ${month.slice(0, 4)} by month`}>
      {cells.map(item => {
        const verdict = verdictOf(item)
        return <button key={item.month} type="button" aria-pressed={item.month === month} title={`${formatMonth(item.month)}: ${verdict.label}`}
          aria-label={`${formatMonth(item.month)}: ${verdict.label}`} onClick={() => onMonth(item.month)}>
          <span className={`climate-strip-swatch ${verdict.color ? '' : 'is-empty'}`} style={verdict.color ? { background: verdict.color } : undefined} />
          <small>{formatMonth(item.month).slice(0, 3)}</small>
        </button>
      })}
    </div>
  </div>
}

/** A weather layer of the trend model at the place: its value in the legend's colour, the month's weather and its year. */
function GridReading({ layer, point, month, onMonth }: { layer: GridLayer; point: DroughtPoint; month: string; onMonth: (month: string) => void }) {
  const cell = point.months.find(item => item.month === month)
  if (point.status !== 'complete' || !cell) return <p className="drought-note">{point.reason ?? 'No value here.'}</p>
  const value = gridValue(layer, cell)
  const band = gridClass(layer, value)
  return <>
    <p className="climate-cell mono">{`${point.cellLatitude?.toFixed(2)}°, ${point.cellLongitude?.toFixed(2)}° grid cell`}</p>
    <div className="climate-verdict" style={{ '--verdict': band?.color ?? 'var(--sage)' } as CSSProperties}>
      <span className="climate-verdict-swatch" aria-hidden="true" />
      <div>
        <p className="climate-verdict-label">{gridText(layer, value)}</p>
        <p className="climate-verdict-sub">{layer.label}{band ? <> · <strong>{band.label}{layer.unit ? ` ${layer.unit}` : ''}</strong></> : null}</p>
      </div>
    </div>
    <Weather cell={cell} />
    <MonthStrip point={point} month={month} onMonth={onMonth} label={layer.label}
      verdict={item => { const amount = gridValue(layer, item); return { label: gridText(layer, amount), color: gridClass(layer, amount)?.color ?? null } }} />
  </>
}

/**
 * A Copernicus or NASA satellite layer at the place, read from the layer's own image, then the month's weather from the
 * trend model for context.
 */
function ObservedReading({ layer, picked, month, point }: {
  layer: Exclude<MapLayer, GridLayer>; picked: { lat: number; lng: number }; month: string; point: DroughtPoint | null
}) {
  const reading = useLayerPoint(layer, picked, month)
  const { time, held } = satelliteTime(layer, month)
  const when = held || !time ? 'Newest image' : layer.kind === 'wms' ? `10-day period from ${time}` : `Image of ${time}`
  const cell = point?.status === 'complete' ? point.months.find(item => item.month === month) : undefined
  const data = reading.data
  return <>
    <p className="climate-cell mono">{when}</p>
    {reading.isPending ? <p className="drought-note"><TractorSpinner size={14} /> Reading {layer.label.toLowerCase()} here…</p>
      : reading.error ? <p className="drought-note is-error">{`Couldn't read ${layer.label.toLowerCase()} here: ${reading.error.message}`}</p>
        : data?.kind === 'cdi' && data.reading ? <div className="climate-verdict" style={{ '--verdict': data.reading.color ?? 'var(--sage)' } as CSSProperties}>
            <span className="climate-verdict-swatch" aria-hidden="true" />
            <div><p className="climate-verdict-label">{data.reading.label}</p><p className="climate-verdict-sub">{data.reading.detail}</p></div>
          </div>
          : data?.kind === 'gibs' && data.reading ? (() => { const text = gibsText(data.reading, layer.id); return <div className="climate-verdict" style={{ '--verdict': data.reading.color } as CSSProperties}>
            <span className="climate-verdict-swatch" aria-hidden="true" />
            <div><p className="climate-verdict-label">{text.value}</p><p className="climate-verdict-sub">{layer.label}. {text.meaning}</p></div>
          </div> })()
            : <div className="climate-verdict is-unrated"><span className="drought-swatch is-empty" />
              <p>{layer.kind === 'wms' ? 'No Copernicus reading here: the indicator covers Europe only, and some cells are left out.'
                : 'No reading here in this image: clouds, snow, water or a gap in the satellite pass.'}</p></div>}
    {cell && cell.status !== 'no_data' && <><h3 className="climate-subhead">Weather this month, Polle trend</h3><Weather cell={cell} /></>}
  </>
}

const round = (value: number) => Math.round(value * 10_000) / 10_000
const monthIndex = (month: string) => Number(month.slice(0, 4)) * 12 + Number(month.slice(5, 7))
