import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router'
import { ArrowLeft, Check, MapPin } from 'lucide-react'
import type { LatLngLiteral } from 'leaflet'
import { problemMessage, useCreateFarm } from '@/features/farms/api'
import { HomeMarker, PointPicker } from '@/features/farms/FarmMapLayers'
import { formatCoordinates, toPoint } from '@/features/farms/geo'
import { BaseMap } from '@/features/map/BaseMap'
import { PlaceSearch } from '@/features/map/PlaceSearch'
import { usePlaceName } from '@/features/map/geocode'
import { TractorSpinner } from '@/shared/ui/Tractor'

type Errors = Partial<Record<'name' | 'location' | 'description' | 'homePoint', string>>

export function NewFarmPage() {
  const navigate = useNavigate()
  const createFarm = useCreateFarm()
  const [values, setValues] = useState({ name: '', location: '', description: '' })
  const [point, setPoint] = useState<LatLngLiteral | null>(null)
  const [errors, setErrors] = useState<Errors>({})
  const [formError, setFormError] = useState('')
  // The location follows the farm point's nearest place until the farmer edits the box; from then on it is theirs.
  const place = usePlaceName(point)
  const [locationTyped, setLocationTyped] = useState(false)
  const location = locationTyped ? values.location : place.label ?? values.location

  async function submit(event: FormEvent) {
    event.preventDefault()
    const next: Errors = {}
    if (!values.name.trim()) next.name = 'Give the farm a name.'
    if (!point) next.homePoint = 'Place the farm on the map. Fields are drawn around this point.'
    setErrors(next); setFormError('')
    if (next.name) { document.getElementById('farm-name')?.focus(); return }
    if (!point) return
    try {
      const farm = await createFarm.mutateAsync({ name: values.name.trim(), location: location.trim() || null,
        description: values.description.trim() || null, homePoint: toPoint(point) })
      navigate(`/dashboard/farms/${farm.id}`, { replace: true })
    } catch (error) {
      const body = (error as { body?: { errors?: Record<string, string[]> } }).body
      if (body?.errors) setErrors(Object.fromEntries(Object.entries(body.errors).map(([key, messages]) => [key, messages[0]])))
      else setFormError(problemMessage(error))
    }
  }

  const set = (key: keyof typeof values) => (event: { target: { value: string } }) => {
    setValues({ ...values, [key]: event.target.value }); if (errors[key]) setErrors({ ...errors, [key]: undefined })
    if (key === 'location') setLocationTyped(true)
  }
  const locationHint = locationTyped ? null
    : place.status === 'looking' ? 'Finding the nearest place…'
    : place.status === 'found' ? 'Filled in from the farm point. Edit it if you call the place differently.'
    : place.status === 'none' ? 'No named place near this point. Type one if you like.'
    : place.status === 'failed' ? 'The place name couldn’t be looked up. Type it if you like.'
    : 'Filled in from the farm point once you place it.'
  const pick = (position: LatLngLiteral) => { setPoint(position); setErrors(current => ({ ...current, homePoint: undefined })) }
  const pending = createFarm.isPending

  return <div className="farm-atlas farm-create">
    <aside className="atlas-panel">
      <header className="atlas-panel-head">
        <Link to="/dashboard/farms" className="subtle-link"><ArrowLeft size={14} />All farms</Link>
        <h1>New farm</h1>
        <p className="atlas-location">Name the farm and mark where it is. The map opens on this point every time you return.</p>
      </header>
      <form className="farm-form" onSubmit={submit} noValidate aria-busy={pending}>
        <div className="form-field">
          <label htmlFor="farm-name">Farm name</label>
          <input id="farm-name" value={values.name} onChange={set('name')} maxLength={150} required disabled={pending}
            aria-invalid={Boolean(errors.name)} aria-describedby={errors.name ? 'farm-name-error' : undefined} autoFocus />
          {errors.name && <p id="farm-name-error" className="field-error" role="alert">{errors.name}</p>}
        </div>
        <div className={`point-field ${point ? 'is-set' : ''} ${errors.homePoint ? 'is-invalid' : ''}`} aria-live="polite">
          <MapPin size={17} />
          <div>
            <span className="point-field-label">Farm point</span>
            {point ? <span className="mono">{formatCoordinates(toPoint(point))}</span> : <span>Click the map to place it</span>}
          </div>
          {point && <Check size={16} className="point-field-check" />}
        </div>
        {errors.homePoint && <p className="field-error" role="alert">{errors.homePoint}</p>}
        <div className="form-field">
          <label htmlFor="farm-location">Location <span className="optional">optional</span></label>
          <input id="farm-location" value={location} onChange={set('location')} maxLength={500} disabled={pending}
            placeholder="e.g. Poltava oblast, Ukraine" aria-invalid={Boolean(errors.location)}
            aria-describedby={locationHint ? 'farm-location-hint' : undefined} />
          {errors.location ? <p className="field-error" role="alert">{errors.location}</p>
            : locationHint && <p id="farm-location-hint" className="form-hint" aria-live="polite">{locationHint}</p>}
        </div>
        <div className="form-field">
          <label htmlFor="farm-description">Notes <span className="optional">optional</span></label>
          <textarea id="farm-description" value={values.description} onChange={set('description')} maxLength={2000} rows={3} disabled={pending}
            aria-invalid={Boolean(errors.description)} />
          {errors.description && <p className="field-error" role="alert">{errors.description}</p>}
        </div>
        {formError && <div className="form-error" role="alert">{formError}</div>}
        <button type="submit" className="button button-dark farm-submit" disabled={pending}>
          {pending ? <><TractorSpinner size={17} />Creating farm…</> : <><Check size={17} />Create farm</>}</button>
      </form>
    </aside>
    <BaseMap className="is-drawing" label="Map for placing the farm point"
      overlay={!point && <div className="map-banner" role="status"><p>Find your land, then click to place the farm point.</p></div>}>
      {!pending && <PlaceSearch onLocated={pick} />}
      {!pending && <PointPicker onPick={pick} />}
      {point && <HomeMarker position={point} label={values.name.trim() || 'New farm'} movable={!pending} onMove={pick} />}
    </BaseMap>
  </div>
}
