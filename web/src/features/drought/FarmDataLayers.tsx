import { useEffect, useState } from 'react'
import { useMap } from 'react-leaflet'
import { ChevronLeft, ChevronRight, Layers, X } from 'lucide-react'
import { Link } from 'react-router'
import { ApiError } from '@/shared/api/client'
import { MapControl } from '@/features/map/BaseMap'
import { useHideAssistant } from '@/features/assistant/useHideAssistant'
import { currentMonth, defaultMonth, layerSource, mapLayers, sameMonthAhead, useDroughtOverview, useDroughtPoint, type DroughtCell, type DroughtOverview } from './api'
import { celsius, cellVerdict, formatMonth, kindLabels, layerDateNote, mm, percentileText } from './format'
import { LayerChooser, LayerLegend, MapDataLayer } from './MapDataLayer'
import { TractorSpinner } from '@/shared/ui/Tractor'

type LatLng = { lat: number; lng: number }

/** Optional data layers for the farm map (drought, weather, satellite), with a plain-language reading at the farm. */
export function FarmDataLayers({ point }: { point: LatLng | null }) {
  const [enabled, setEnabled] = useState(false)
  const overview = useDroughtOverview(enabled)
  // The open panel runs down the map's right edge, where the floating assistant sits.
  useHideAssistant(enabled)
  // Once the panel has its size, the selected field is moved out from under it (see FlyTo).
  const map = useMap()
  const ready = Boolean(overview.data)
  useEffect(() => {
    if (!enabled) return
    const frame = requestAnimationFrame(() => map.fire('layerspanelopen'))
    return () => cancelAnimationFrame(frame)
  }, [enabled, ready, map])
  return <MapControl className={`farm-drought ${enabled ? 'is-open' : ''}`}>
    <button type="button" className="farm-drought-toggle" aria-expanded={enabled} aria-controls="farm-layers-body" onClick={() => setEnabled(value => !value)}>
      <Layers size={15} />Layers{enabled && <X size={14} className="farm-drought-close" aria-hidden="true" />}</button>
    {enabled && <div id="farm-layers-body" className="farm-drought-body">{overview.isPending ? <p className="farm-drought-note"><TractorSpinner size={14} /> Loading map layers…</p>
      : overview.error ? <p className="farm-drought-note">{overview.error instanceof ApiError && overview.error.status === 503
        ? 'Map layers aren’t available on this server yet.' : overview.error.message}</p>
        : overview.data.months.length === 0 ? <p className="farm-drought-note">No months in the climate model.</p>
          : <LayersCard overview={overview.data} point={point} />}</div>}
  </MapControl>
}

// Projections drift further from any real year the further ahead they go; fade those years.
const SURE_YEARS_AHEAD = 5

function LayersCard({ overview, point }: { overview: DroughtOverview; point: LatLng | null }) {
  const layers = mapLayers(overview)
  const [layerId, setLayerId] = useState('drought')
  const layer = layers.find(item => item.id === layerId) ?? layers[0]
  const [month, setMonth] = useState(() => defaultMonth(overview))
  const { months } = overview
  const position = months.findIndex(item => item.month === month)
  const kind = months[position].kind
  const reading = useDroughtPoint(point)
  const series = reading.data?.months ?? []
  const cell = series.find(item => item.month === month) ?? null
  const now = currentMonth()
  const ahead = sameMonthAhead(series, month, now)
  const verdict = cellVerdict(cell, overview.classes, overview.noDrought?.color)
  const dateNote = layerDateNote(layer, month)

  return <>
    <MapDataLayer overview={overview} layer={layer} month={month} opacity={layer.kind === 'grid' ? 0.45 : 0.7} />
    <LayerChooser overview={overview} layers={layers} value={layer.id} onChange={setLayerId} name="farm-layer" compact />
    <div className="farm-drought-day">
      <button type="button" aria-label="Previous month" disabled={position <= 0} onClick={() => setMonth(months[position - 1].month)}><ChevronLeft size={15} /></button>
      <span><strong>{formatMonth(month)}</strong><small>{month === now ? 'This month · ' : ''}{kindLabels[kind]}</small></span>
      <button type="button" aria-label="Next month" disabled={position >= months.length - 1} onClick={() => setMonth(months[position + 1].month)}><ChevronRight size={15} /></button>
    </div>
    {dateNote && <p className="farm-drought-note">{dateNote}</p>}

    {!point ? <p className="farm-drought-note">Set the farm point to see conditions at your farm.</p>
      : <div className="farm-drought-reading" aria-live="polite">
        <span className="farm-drought-label">At this farm</span>
        {reading.isPending ? <p className="farm-drought-note"><TractorSpinner size={14} /> Reading…</p>
          : <>
            <p className="farm-drought-verdict"><span className="drought-swatch" style={{ background: verdict.color ?? 'transparent' }} />{verdict.label}</p>
            {cell?.status === 'ok' && <p className="farm-drought-detail">A typical 30 days: {mm(cell.precipitation30dMm)} of rain against {mm(cell.et030dMm)} of evaporation demand. {seasonComparison(cell.percentile)}</p>}
            {cell?.status === 'arid' && <p className="farm-drought-detail">It normally barely rains here at this time of year, so drought isn’t rated.</p>}
            {cell && cell.status !== 'no_data' && <p className="farm-drought-detail">{weatherLine(cell)}</p>}
          </>}
      </div>}

    {point && ahead.length > 1 && <div className="farm-drought-strip" aria-label="This month over the coming years at this farm">
      {ahead.map((item, i) => {
        const itemVerdict = cellVerdict(item, overview.classes, overview.noDrought?.color)
        const uncertain = i > SURE_YEARS_AHEAD
        return <button key={item.month} type="button" aria-pressed={item.month === month} onClick={() => setMonth(item.month)}
          className={uncertain ? 'is-uncertain' : undefined}
          title={`${formatMonth(item.month)}: ${itemVerdict.label}, ${celsius(item.tmeanC)}, ${mm(item.precipitation30dMm)} rain${uncertain ? ' (less certain)' : ''}`}>
          <span className="drought-swatch" style={{ background: itemVerdict.color ?? 'transparent' }} />
          <span className="farm-strip-temp">{item.tmeanC != null ? `${Math.round(item.tmeanC)}°` : '—'}</span>
          <small>{item.month.slice(0, 4)}</small></button>
      })}
    </div>}

    {layer.id === 'drought'
      ? <ul className="farm-drought-legend" aria-label="Drought levels">{overview.classes.map(item =>
        <li key={item.code} title={`${item.label}: driest ${item.percentile_max}% of the season`}><span className="drought-swatch" style={{ background: item.color }} />{item.code}</li>)}
        {overview.noDrought && <li title="Rated, not in drought"><span className="drought-swatch" style={{ background: overview.noDrought.color }} />None</li>}</ul>
      : <LayerLegend layer={layer} />}
    <p className="farm-drought-note">Source: {layerSource(layer, overview).provider}.{' '}
      {layer.kind === 'grid' ? 'An expected climate from the last decade’s trend, in cells about 50 km wide.' : layer.description}{' '}
      <Link to="/dashboard/climate">World map and method</Link></p>
  </>
}

function weatherLine(cell: DroughtCell) {
  return `Air ${celsius(cell.tmeanC)} (${celsius(cell.tminC)} to ${celsius(cell.tmaxC)}), ${mm(cell.precipitation30dMm)} of rain in 30 days.`
}

function seasonComparison(percentile: number | null) {
  if (percentile === null || percentile >= 50) return 'That is normal or wetter for this time of year.'
  return `Only ${percentileText(percentile)} percentile: drier than about ${100 - Math.round(percentile)}% of the same weeks in 1991–2020.`
}
