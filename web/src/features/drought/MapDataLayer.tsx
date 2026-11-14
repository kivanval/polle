import { ImageOverlay, TileLayer, WMSTileLayer } from 'react-leaflet'
import { copernicusWms, layerImageUrl, layerSource, layerThemes, satelliteTime, themeOf, type DroughtOverview, type MapLayer } from './api'

/** The chosen layer on the map for a month: a model image, NASA GIBS tiles or Copernicus WMS tiles. */
export function MapDataLayer({ overview, layer, month, opacity }: { overview: DroughtOverview; layer: MapLayer; month: string; opacity: number }) {
  if (layer.kind === 'grid') {
    const url = layerImageUrl(overview, layer.id, month)
    return <ImageOverlay key={url} url={url} bounds={overview.imageBounds} opacity={opacity} className="data-layer-image" />
  }
  const { time } = satelliteTime(layer, month)
  if (layer.kind === 'wms') {
    // The Copernicus server only accepts upper-case WMS parameter names; without a time it shows the newest period.
    return <WMSTileLayer key={time ?? 'latest'} url={copernicusWms}
      params={{ layers: 'cdiad,cdirc', format: 'image/png', transparent: true, ...(time ? { time } : {}) }}
      uppercase opacity={opacity} attribution='Drought: <a href="https://drought.emergency.copernicus.eu/">Copernicus EDO</a>' />
  }
  // GIBS serves its newest image for the time "default".
  const url = layer.tiles.replace('{layer}', layer.gibs_layer).replace('{time}', time ?? 'default').replace('{matrix}', layer.tile_matrix_set)
  return <TileLayer key={url} url={url} maxNativeZoom={layer.max_native_zoom} maxZoom={20} opacity={opacity}
    attribution='Satellite layers: <a href="https://www.earthdata.nasa.gov/gibs">NASA GIBS</a>' />
}

/**
 * Two steps: a theme (drought, weather, land from orbit), then one of its datasets. Datasets that measure the same
 * theme differently, such as the worldwide drought trend and the European CDI, stay separate choices with their
 * source and coverage shown, so the map always says which dataset it is drawing.
 */
export function LayerChooser({ overview, layers, value, onChange, name, compact = false }: {
  overview: DroughtOverview; layers: MapLayer[]; value: string; onChange: (id: string) => void; name: string; compact?: boolean
}) {
  const themes = layerThemes(layers)
  const current = layers.find(layer => layer.id === value) ?? layers[0]
  const theme = themes.find(item => item.id === themeOf(current)) ?? themes[0]
  return <div className={`layer-chooser ${compact ? 'is-compact' : ''}`}>
    <div className="layer-themes" role="group" aria-label="Map theme">
      {themes.map(item => <button key={item.id} type="button" aria-pressed={item.id === theme.id}
        onClick={() => { if (item.id !== theme.id) onChange(item.layers[0].id) }}>{item.label}</button>)}
    </div>
    <div className="layer-picker" role="radiogroup" aria-label={`${theme.label} datasets`}>
      {theme.layers.map(layer => {
        const source = layerSource(layer, overview)
        return <label key={layer.id} className="layer-option" title={source.provider}>
          <input type="radio" name={name} value={layer.id} checked={layer.id === current.id} onChange={() => onChange(layer.id)} />
          <span><strong>{theme.id === 'drought' ? source.name : layer.label}</strong>
            <small>{source.coverage} · {source.cadence}</small>
            {!compact && <small className="layer-provider">{source.provider}</small>}</span>
        </label>
      })}
    </div>
  </div>
}

/** Colour key for a non-drought layer; drought keeps its own detailed key. */
export function LayerLegend({ layer }: { layer: MapLayer }) {
  if (layer.kind === 'grid') return <ul className="layer-legend" aria-label={`${layer.label} key`}>
    {layer.legend.map(item => <li key={item.label}><span className="drought-swatch" style={{ background: item.color }} />{item.label}{layer.unit ? ` ${layer.unit}` : ''}</li>)}
  </ul>
  return <img className="layer-legend-image" src={layer.legend_url} alt={`${layer.label} colour key`} />
}
