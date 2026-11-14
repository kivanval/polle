import { useCallback, useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { Link, useParams, useSearchParams } from 'react-router'
import { ArrowLeft, Check, MapPinned, PenLine, PencilLine, X, Tractor } from 'lucide-react'
import type { LatLng, LatLngBoundsLiteral, LatLngLiteral, LatLngTuple, Polygon as LeafletPolygon } from 'leaflet'
import type { MultiPolygon, Polygon } from 'geojson'
import { mappedHectares, nestFields, topLevelFields, useDeleteField, useFarm, useFields, useRenameFarm, useSaveField, useUpdateFarm, problemMessage, type Farm, type Field, type PolygonGeometry } from '@/features/farms/api'
import { FieldList } from '@/features/farms/FieldList'
import { FieldInspector } from '@/features/farms/FieldInspector'
import { useNamedPage } from '@/app/pageTitle'
import { conditionsOf, NO_CONDITIONS, type FieldConditions } from '@/features/farms/conditions'
import { FieldConditionsFields } from '@/features/farms/FieldConditionsFields'
import { farmResourcesPath } from '@/features/farms/resources'
import { cropName, currentPlanting, useCrops, useFarmPlantings } from '@/features/farms/plantings'
import { FieldShape, FlyTo, HomeMarker, PointPicker, RecenterControl, SelectionHalo, type ShapeState } from '@/features/farms/FarmMapLayers'
import { crossesItself, findOverlap, formatCoordinates, formatHectares, hectares, liesInside, MAX_VERTICES, overlapCandidates, pointLatLng, toLatLngs, toPoint, toPolygon, vertexCount } from '@/features/farms/geo'
import { BaseMap } from '@/features/map/BaseMap'
import { ClaimEffect, RejectEffect } from '@/features/map/ClaimEffect'
import { DrawPolygon } from '@/features/map/DrawPolygon'
import { PlaceSearch } from '@/features/map/PlaceSearch'
import { reverseGeocode, usePlaceName, type PlaceLookup } from '@/features/map/geocode'
import { useAssistantPageSelection } from '@/features/assistant/useAssistantPageSelection'
import { FarmDataLayers } from '@/features/drought/FarmDataLayers'
import { PLAN_PARAM } from '@/features/strategies/planParam'
import { TractorLoader, TractorSpinner } from '@/shared/ui/Tractor'

type Mode =
  | { type: 'browse' }
  | { type: 'drawing'; parent: Field | null }
  | { type: 'naming'; boundary: PolygonGeometry; hectares: number; parent: Field | null }
  | { type: 'editing'; fieldId: string }
  | { type: 'placing' }
type Rejection = { boundary: PolygonGeometry; conflict: PolygonGeometry[]; message: string }

export function FarmAtlasPage() {
  const { farmId = '' } = useParams()
  const farm = useFarm(farmId)
  const fields = useFields(farmId)
  // The tab and the top bar name the farm, never its id: "Farm" until it loads, and if it can't.
  useNamedPage(farm.data?.name ?? (farm.error ? 'Farm' : null))
  if (farm.isPending || fields.isPending) return <div className="atlas-state"><TractorLoader /><p>Opening your farm…</p></div>
  if (farm.error || fields.error) {
    const notFound = (farm.error as { status?: number } | null)?.status === 404
    return <div className="atlas-state"><h1>{notFound ? 'This farm isn’t in your workspace.' : 'Your farm didn’t load.'}</h1>
      <p>{notFound ? 'It may have been deleted, or the link is from another account.' : problemMessage(farm.error ?? fields.error)}</p>
      {notFound ? <Link className="button button-dark" to="/dashboard/farms">Go to your farms</Link>
        : <button className="button button-dark" onClick={() => { void farm.refetch(); void fields.refetch() }}>Try again</button>}</div>
  }
  return <FarmAtlas key={farmId} farm={farm.data} fields={fields.data} />
}

function FarmAtlas({ farm, fields }: { farm: Farm; fields: Field[] }) {
  const { setSelection } = useAssistantPageSelection()
  const saveField = useSaveField(farm.id)
  const deleteField = useDeleteField(farm.id)
  const updateFarm = useUpdateFarm(farm.id)
  const { data: plantings = [] } = useFarmPlantings(farm.id)
  const { data: crops = [] } = useCrops()
  const mutating = saveField.isPending || deleteField.isPending || updateFarm.isPending
  const [mode, setMode] = useState<Mode>(farm.homePoint ? { type: 'browse' } : { type: 'placing' })
  // A link from the Fields page (?field=…) opens with that field selected and in view; so does a reload while its
  // plan view is open (?plan=…), so the view can reopen.
  const [params] = useSearchParams()
  const [linked] = useState(() => fields.find(field => field.id === (params.get('field') ?? params.get(PLAN_PARAM))) ?? null)
  const [selectedId, setSelectedId] = useState<string | null>(linked?.id ?? null)
  useEffect(() => {
    setSelection({ farmId: farm.id, fieldId: selectedId })
    return () => setSelection(null)
  }, [farm.id, selectedId, setSelection])
  const [hoveredId, setHoveredId] = useState<string | null>(null)
  const [savedId, setSavedId] = useState<string | null>(null)
  const [focus, setFocus] = useState<LatLngBoundsLiteral | null>(() => linked ? boundsOf(toLatLngs(linked.boundary)) : null)
  const [rejection, setRejection] = useState<Rejection | null>(null)
  const [pendingPoint, setPendingPoint] = useState<LatLngLiteral | null>(null)
  // The place at the point being placed: it becomes the farm's location (see savePoint).
  const place = usePlaceName(mode.type === 'placing' ? pendingPoint : null)
  const [useNewPlace, setUseNewPlace] = useState(false)
  const [rowError, setRowError] = useState('')
  const [resetKey, setResetKey] = useState(0)
  const layers = useRef(new Map<string, LeafletPolygon>())
  const register = useCallback((fieldId: string, layer: LeafletPolygon | null) => {
    if (layer) layers.current.set(fieldId, layer); else layers.current.delete(fieldId)
  }, [])

  const home = farm.homePoint ? pointLatLng(farm.homePoint) : null
  // Subfields lie inside their parent: the farm's mapped area counts top-level fields only.
  const totalHectares = mappedHectares(fields)
  const topLevel = topLevelFields(fields)
  const subfieldCount = fields.length - topLevel.length
  const [initialView] = useState(() => initialMapView(farm, fields))

  useEffect(() => {
    if (!rejection) return
    const timer = setTimeout(() => setRejection(null), 6000)
    return () => clearTimeout(timer)
  }, [rejection])
  useEffect(() => {
    if (!savedId) return
    const timer = setTimeout(() => setSavedId(null), 2200)
    return () => clearTimeout(timer)
  }, [savedId])

  const select = useCallback((fieldId: string) => {
    setSelectedId(fieldId); setRowError('')
    const field = fields.find(candidate => candidate.id === fieldId)
    if (field) setFocus(boundsOf(toLatLngs(field.boundary)))
  }, [fields])

  function reject(boundary: PolygonGeometry, message: string, conflict: PolygonGeometry[] = []) {
    setRejection({ boundary, conflict, message })
  }

  const drawingParent = mode.type === 'drawing' ? mode.parent : null
  const drawn = useCallback((latLngs: LatLng[]) => {
    const boundary = toPolygon(latLngs)
    const parent = drawingParent
    setMode({ type: 'browse' })
    if (vertexCount(boundary) < 3) return
    if (crossesItself(boundary)) { reject(boundary, 'The outline crosses itself. Draw the corners in order around the field.'); return }
    if (vertexCount(boundary) > MAX_VERTICES) { reject(boundary, `A field can have up to ${MAX_VERTICES} corners. Draw it with fewer points.`); return }
    if (parent && !liesInside(boundary, parent.boundary)) {
      reject(boundary, `A subfield must lie inside “${parent.name}”. Draw its corners within the field’s outline.`, [parent.boundary])
      return
    }
    const overlap = findOverlap(boundary, overlapCandidates(fields, parent?.id ?? null))
    if (overlap) {
      reject(boundary, parent ? `This outline overlaps “${overlap.field.name}”. Subfields of one field must not overlap each other.`
        : `This outline overlaps “${overlap.field.name}”. Fields can share an edge, but not land.`, polygonsOf(overlap.shape.geometry))
      return
    }
    setRejection(null)
    setMode({ type: 'naming', boundary, hectares: hectares(boundary), parent })
  }, [fields, drawingParent])
  const cancelDrawing = useCallback(() => setMode({ type: 'browse' }), [])

  async function createField(name: string, conditions: FieldConditions, boundary: PolygonGeometry, parent: Field | null) {
    try {
      const field = await saveField.mutateAsync({ input: { name, description: null, boundary, parentFieldId: parent?.id ?? null, ...conditions } })
      setMode({ type: 'browse' }); setSelectedId(field.id); setSavedId(field.id)
      return ''
    } catch (error) { return problemMessage(error, 'name') }
  }

  async function saveBoundary(field: Field) {
    const layer = layers.current.get(field.id)
    if (!layer) return
    const boundary = toPolygon(layer.getLatLngs()[0] as LatLng[])
    if (vertexCount(boundary) < 3 || vertexCount(boundary) > MAX_VERTICES) { reject(boundary, `Use 3–${MAX_VERTICES} corners for the field boundary.`); return }
    if (crossesItself(boundary)) { reject(boundary, 'The outline crosses itself. Move the corners so the edges don’t cross.'); return }
    const parent = field.parentFieldId ? fields.find(candidate => candidate.id === field.parentFieldId) ?? null : null
    if (parent && !liesInside(boundary, parent.boundary)) {
      reject(boundary, `A subfield must stay inside “${parent.name}”. Move the corners back within the field.`, [parent.boundary])
      return
    }
    const outside = fields.find(child => child.parentFieldId === field.id && !liesInside(child.boundary, boundary))
    if (outside) {
      reject(boundary, `Subfield “${outside.name}” would fall outside the new outline. Keep the field around its subfields.`, [outside.boundary])
      return
    }
    const overlap = findOverlap(boundary, overlapCandidates(fields, parent?.id ?? null, field.id), field.id)
    if (overlap) {
      reject(boundary, `The new outline overlaps “${overlap.field.name}”. Move the corners back inside your field.`, polygonsOf(overlap.shape.geometry))
      return
    }
    try {
      await saveField.mutateAsync({ fieldId: field.id, input: { name: field.name, description: field.description, boundary, ...conditionsOf(field) } })
      setMode({ type: 'browse' }); setSavedId(field.id); setRejection(null); setResetKey(key => key + 1)
    } catch (error) { reject(boundary, problemMessage(error, 'boundary')) }
  }

  function cancelEdit() {
    setMode({ type: 'browse' }); setRejection(null); setResetKey(key => key + 1)
  }

  async function rename(field: Field, name: string) {
    try {
      await saveField.mutateAsync({ fieldId: field.id, input: { name, description: field.description, boundary: field.boundary, ...conditionsOf(field) } })
      setSavedId(field.id); setRowError('')
      return true
    } catch (error) { setRowError(problemMessage(error, 'name')); return false }
  }

  async function saveConditions(field: Field, conditions: FieldConditions) {
    try {
      await saveField.mutateAsync({ fieldId: field.id, input: { name: field.name, description: field.description, boundary: field.boundary, ...conditions } })
      setSavedId(field.id); setRowError('')
      return true
    } catch (error) { setRowError(problemMessage(error)); return false }
  }

  async function remove(field: Field) {
    try { await deleteField.mutateAsync(field.id); setSelectedId(null); setRowError(''); return '' }
    catch (error) { return problemMessage(error) }
  }

  async function savePoint() {
    if (!pendingPoint) return
    // A farm without a location takes the new point's place name; one with a location takes it only when asked.
    const placeName = place.label ?? (farm.location && !useNewPlace ? null : await reverseGeocode(pendingPoint).catch(() => null))
    const location = placeName && (!farm.location || useNewPlace) ? placeName : farm.location
    try {
      await updateFarm.mutateAsync({ name: farm.name, description: farm.description, location, homePoint: toPoint(pendingPoint) })
      setMode({ type: 'browse' }); setPendingPoint(null); setUseNewPlace(false)
    } catch { /* message rendered from updateFarm.error */ }
  }

  const editingField = mode.type === 'editing' ? fields.find(field => field.id === mode.fieldId) ?? null : null
  const selectedField = fields.find(field => field.id === selectedId) ?? null
  const shapeState = (field: Field): ShapeState => field.id === selectedId ? 'selected'
    : field.id === hoveredId ? 'hovered' : selectedId ? 'dimmed' : 'idle'
  const placing = mode.type === 'placing'
  const currentCropName = (fieldId: string) => {
    const current = currentPlanting(plantings.filter(planting => planting.fieldId === fieldId))
    return current ? cropName(crops, current.cropId) : undefined
  }
  const markerPosition = placing ? pendingPoint ?? home : home

  return <div className="farm-atlas field-atlas">
    <aside className="atlas-panel" aria-label={`${farm.name} fields`}>
      <header className="atlas-panel-head">
        <Link to="/dashboard/farms" className="subtle-link"><ArrowLeft size={14} />All farms</Link>
        <FarmName farm={farm} disabled={mode.type !== 'browse' || mutating} />
        {farm.location && <p className="atlas-location">{farm.location}</p>}
        <dl className="atlas-totals">
          <div><dt>Fields</dt><dd>{topLevel.length}{subfieldCount > 0 && <small> + {subfieldCount} sub</small>}</dd></div>
          <div><dt>Mapped area</dt><dd>{formatHectares(totalHectares)}</dd></div>
        </dl>
        {farm.homePoint && !placing && <button type="button" className="atlas-point" disabled={mode.type !== 'browse' || mutating} onClick={() => { setMode({ type: 'placing' }); setPendingPoint(null) }}>
          <MapPinned size={14} /><span className="mono">{formatCoordinates(farm.homePoint)}</span><span>Move</span></button>}
        <Link to={farmResourcesPath(farm.id)} className="atlas-point atlas-resources">
          <Tractor size={14} /><span>Budget, machinery and hours</span><span>Open</span></Link>
      </header>

      {mode.type === 'naming'
        ? <NameFieldForm key={mode.boundary.coordinates[0].length} hectares={mode.hectares} corners={vertexCount(mode.boundary)} parent={mode.parent}
            suggestion={mode.parent ? subfieldName(mode.parent, fields) : `Field ${topLevel.length + 1}`} onDiscard={() => setMode({ type: 'browse' })}
            onSave={(name, conditions) => createField(name, conditions, mode.boundary, mode.parent)} />
        : fields.length === 0
          ? <div className="atlas-empty">
              <h2>Outline your first field.</h2>
              <p>Click each corner of the field on the map, then click the first corner again to close it. Corners snap to the edges of neighbouring fields.</p>
            </div>
          : <FieldList fields={fields} plantings={plantings} crops={crops} selectedId={selectedId} hoveredId={hoveredId} savedId={savedId} busy={mutating || mode.type !== 'browse'}
              onSelect={select} onHover={setHoveredId} />}

      {mode.type !== 'naming' && <footer className="atlas-panel-foot">
        {mode.type === 'drawing'
          ? <button type="button" className="button button-outline" onClick={cancelDrawing}><X size={17} />Cancel drawing</button>
          : <button type="button" className="button button-dark" disabled={mode.type !== 'browse' || mutating}
              onClick={() => { setMode({ type: 'drawing', parent: null }); setSelectedId(null); setRejection(null) }}><PenLine size={17} />Draw a field</button>}
      </footer>}
    </aside>

    <BaseMap className={mode.type === 'drawing' || placing ? 'is-drawing' : ''} label={`Map of ${farm.name}`}
      center={initialView.center} zoom={initialView.zoom} bounds={initialView.bounds}
      overlay={<>
        {mode.type === 'drawing' && <MapBanner>{mode.parent
          ? <>Draw a subfield inside <strong>{mode.parent.name}</strong>. Corners snap to its edges. Click the first corner to close it. <kbd>Esc</kbd> cancels.</>
          : <>Click to place corners. Click the first corner to close the field. <kbd>Esc</kbd> cancels.</>}</MapBanner>}
        {editingField && <MapBanner actions={<>
          <button type="button" className="button button-lime" disabled={saveField.isPending} onClick={() => void saveBoundary(editingField)}>
            {saveField.isPending ? <TractorSpinner size={15} /> : <Check size={15} />}Save boundary</button>
          <button type="button" className="button button-ghost" disabled={saveField.isPending} onClick={cancelEdit}>Cancel</button></>}>
          Drag corners to reshape <strong>{editingField.name}</strong>. Drag a midpoint to add a corner. Right-click a corner to remove it.
        </MapBanner>}
        {placing && <MapBanner tone={updateFarm.error ? 'error' : undefined} actions={<>
          <button type="button" className="button button-lime" disabled={!pendingPoint || updateFarm.isPending} onClick={() => void savePoint()}>
            {updateFarm.isPending ? <TractorSpinner size={15} /> : <Check size={15} />}Save farm point</button>
          {farm.homePoint && <button type="button" className="button button-ghost" disabled={updateFarm.isPending} onClick={() => { setMode({ type: 'browse' }); setPendingPoint(null) }}>Cancel</button>}</>}>
          {updateFarm.error ? problemMessage(updateFarm.error, 'homePoint')
            : farm.homePoint ? 'Click the map or drag the marker to move the farm point.'
            : 'This farm has no point yet. Click the map where the farm is, usually the yard or main entrance.'}
          {pendingPoint && !updateFarm.error && <PlaceNote place={place} current={farm.location} replace={useNewPlace} onReplace={setUseNewPlace} />}
        </MapBanner>}
        {rejection && <MapBanner tone="error" actions={<button type="button" className="button button-ghost" onClick={() => setRejection(null)}>Dismiss</button>}>
          {rejection.message}</MapBanner>}
      </>}>
      <RecenterControl home={home} />
      <FarmDataLayers point={farmPoint(home, fields)} />
      <FlyTo bounds={focus} />
      <SelectionHalo field={mode.type === 'browse' ? fields.find(field => field.id === selectedId) ?? null : drawingParent} />
      <DrawPolygon active={mode.type === 'drawing'} onDrawn={drawn} onCancel={cancelDrawing} />
      {placing && !updateFarm.isPending && <PointPicker onPick={setPendingPoint} />}
      {placing && !updateFarm.isPending && <PlaceSearch onLocated={setPendingPoint} />}
      {/* Parents first, so their subfields are drawn above them and receive the clicks. */}
      {nestFields(fields).map(field => <FieldShape key={`${field.id}:${field.updatedAtUtc}:${resetKey}`} field={field} state={shapeState(field)}
        editing={editingField?.id === field.id && !saveField.isPending} cropName={currentCropName(field.id)} onSelect={mode.type === 'browse' && !mutating ? select : () => {}} onHover={setHoveredId} register={register} />)}
      {markerPosition && <HomeMarker position={markerPosition} label={mode.type === 'browse' || placing ? farm.name : ''} movable={placing && !updateFarm.isPending} onMove={setPendingPoint} />}
      {mode.type === 'naming' && <ClaimEffect boundary={mode.boundary} hectares={mode.hectares} />}
      {rejection && <RejectEffect key={JSON.stringify(rejection.boundary.coordinates[0][0])} boundary={rejection.boundary} conflict={rejection.conflict} />}
    </BaseMap>

    {/* The selected field's actions, docked over the map instead of opening inside the list. Hidden while drawing or
        reshaping, when the map banner holds the controls. */}
    {mode.type === 'browse' && selectedField && <FieldInspector key={selectedField.id} field={selectedField} fields={fields} plantings={plantings} crops={crops}
      busy={mutating} error={rowError} onClose={() => { setSelectedId(null); setRowError('') }} onSelect={select} onRename={rename} onSaveConditions={saveConditions}
      onEditBoundary={field => { setMode({ type: 'editing', fieldId: field.id }); setRejection(null) }} onDelete={remove}
      onAddSubfield={field => { setMode({ type: 'drawing', parent: field }); setFocus(boundsOf(toLatLngs(field.boundary))); setRejection(null) }} />}
  </div>
}

/** The farm's name with an inline rename, like a field's. */
function FarmName({ farm, disabled }: { farm: Farm; disabled: boolean }) {
  const rename = useRenameFarm(farm.id)
  const [editing, setEditing] = useState(false)
  const [name, setName] = useState(farm.name)
  const [error, setError] = useState('')
  async function submit(event: FormEvent) {
    event.preventDefault()
    if (rename.isPending) return
    const next = name.trim()
    if (!next) { setError('Give the farm a name.'); return }
    if (next === farm.name) { setEditing(false); return }
    try { await rename.mutateAsync(next); setEditing(false) } catch (reason) { setError(problemMessage(reason, 'name')) }
  }
  if (!editing) return <div className="farm-title">
    <h1>{farm.name}</h1>
    <button type="button" className="button button-quiet button-icon" disabled={disabled} aria-label={`Rename ${farm.name}`} title="Rename farm"
      onClick={() => { setName(farm.name); setError(''); setEditing(true) }}><PencilLine size={15} /></button>
  </div>
  return <form className="field-rename farm-rename" onSubmit={submit}>
    <label htmlFor="rename-farm" className="visually-hidden">Farm name</label>
    <input id="rename-farm" value={name} maxLength={150} autoFocus disabled={rename.isPending} aria-invalid={Boolean(error)}
      aria-describedby={error ? 'rename-farm-error' : undefined} onFocus={event => event.target.select()}
      onChange={event => { setName(event.target.value); setError('') }} onKeyDown={event => { if (event.key === 'Escape' && !rename.isPending) setEditing(false) }} />
    {error && <p id="rename-farm-error" className="field-error" role="alert">{error}</p>}
    <div className="field-actions">
      <button type="submit" className="button button-dark" disabled={rename.isPending || !name.trim()}>{rename.isPending && <TractorSpinner size={15} />}Save name</button>
      <button type="button" className="button button-quiet" disabled={rename.isPending} onClick={() => setEditing(false)}>Cancel</button>
    </div>
  </form>
}

/** "North field · A", then B, C… skipping letters already used by the field's subfields. */
function subfieldName(parent: Field, fields: Field[]) {
  const taken = new Set(fields.filter(field => field.parentFieldId === parent.id).map(field => field.name))
  for (let index = 0; index < 26; index++) {
    const name = `${parent.name} · ${String.fromCharCode(65 + index)}`
    if (!taken.has(name)) return name.slice(0, 150)
  }
  return `${parent.name} · part`.slice(0, 150)
}

function NameFieldForm({ hectares: area, corners, suggestion, parent, onSave, onDiscard }: {
  hectares: number; corners: number; suggestion: string; parent: Field | null; onSave: (name: string, conditions: FieldConditions) => Promise<string>; onDiscard: () => void
}) {
  const [name, setName] = useState(suggestion)
  // A subfield starts with its field's conditions: usually the same ground.
  const [conditions, setConditions] = useState<FieldConditions>(() => parent ? conditionsOf(parent) : NO_CONDITIONS)
  const [error, setError] = useState('')
  const [pending, setPending] = useState(false)
  async function submit(event: FormEvent) {
    event.preventDefault()
    if (pending) return
    if (!name.trim()) { setError('Give the field a name.'); return }
    setPending(true); setError(await onSave(name.trim(), conditions)); setPending(false)
  }
  return <form className="name-field" onSubmit={submit} aria-labelledby="name-field-title">
    <p className="name-field-kicker"><Check size={14} />{parent ? `Subfield of ${parent.name}` : 'Boundary closed'}</p>
    <h2 id="name-field-title">{formatHectares(area)}</h2>
    <p className="name-field-meta">{corners} corners.{parent ? ` About ${Math.round((area / parent.areaHectares) * 100)}% of the field. Its area stays part of the field’s total.` : ''} The saved area is recalculated on the server.</p>
    <label htmlFor="new-field-name">{parent ? 'Subfield name' : 'Field name'}</label>
    <input id="new-field-name" value={name} maxLength={150} autoFocus disabled={pending} aria-invalid={Boolean(error)}
      aria-describedby={error ? 'new-field-error' : undefined} onFocus={event => event.target.select()}
      onChange={event => { setName(event.target.value); setError('') }} onKeyDown={event => { if (event.key === 'Escape' && !pending) onDiscard() }} />
    {error && <p id="new-field-error" className="field-error" role="alert">{error}</p>}
    <details className="field-conditions-panel">
      <summary>Soil &amp; climate <span>(optional)</span></summary>
      <p className="field-conditions-note">Strategies use these instead of the estimated soil. Leave “Not sure” if you don’t know.</p>
      <FieldConditionsFields value={conditions} disabled={pending} onChange={setConditions} />
    </details>
    <div className="field-actions">
      <button type="submit" className="button button-dark" disabled={pending}>{pending ? <TractorSpinner size={16} /> : <Check size={16} />}{parent ? 'Save subfield' : 'Save field'}</button>
      <button type="button" className="button button-quiet" onClick={onDiscard} disabled={pending}>Discard</button>
    </div>
  </form>
}

/** Under the farm-point banner: the place found at the new point, and whether it replaces the farm's location. */
function PlaceNote({ place, current, replace, onReplace }: { place: PlaceLookup; current: string | null; replace: boolean; onReplace: (replace: boolean) => void }) {
  if (place.status === 'looking') return <span className="map-banner-place">Finding the nearest place…</span>
  if (place.status !== 'found' || !place.label) return null
  if (!current) return <span className="map-banner-place">Location: <strong>{place.label}</strong></span>
  if (place.label === current) return null
  return <label className="map-banner-place">
    <input type="checkbox" checked={replace} onChange={event => onReplace(event.target.checked)} />
    <span>Change the location to <strong>{place.label}</strong></span>
  </label>
}

function MapBanner({ children, actions, tone }: { children: ReactNode; actions?: ReactNode; tone?: 'error' }) {
  return <div className={`map-banner ${tone === 'error' ? 'is-error' : ''}`} role={tone === 'error' ? 'alert' : 'status'}>
    <p>{children}</p>{actions && <div className="map-banner-actions">{actions}</div>}
  </div>
}

function boundsOf(points: LatLngTuple[]): LatLngBoundsLiteral {
  const lats = points.map(([lat]) => lat), lngs = points.map(([, lng]) => lng)
  return [[Math.min(...lats), Math.min(...lngs)], [Math.max(...lats), Math.max(...lngs)]]
}

function polygonsOf(geometry: Polygon | MultiPolygon): PolygonGeometry[] {
  const rings = geometry.type === 'Polygon' ? [geometry.coordinates] : geometry.coordinates
  return rings.map(coordinates => ({ type: 'Polygon', coordinates: coordinates as PolygonGeometry['coordinates'] }))
}

/** Where the farm's drought is read: the farm point, else the middle of its fields. */
function farmPoint(home: LatLngTuple | null, fields: Field[]): { lat: number; lng: number } | null {
  if (home) return { lat: home[0], lng: home[1] }
  const points = fields.flatMap(field => toLatLngs(field.boundary))
  if (points.length === 0) return null
  const [[south, west], [north, east]] = boundsOf(points)
  return { lat: (south + north) / 2, lng: (west + east) / 2 }
}

function initialMapView(farm: Farm, fields: Field[]): { center?: LatLngTuple; zoom?: number; bounds?: LatLngBoundsLiteral } {
  const points = fields.flatMap(field => toLatLngs(field.boundary))
  if (farm.homePoint) points.push(pointLatLng(farm.homePoint))
  if (fields.length > 0) return { bounds: boundsOf(points) }
  if (farm.homePoint) return { center: pointLatLng(farm.homePoint), zoom: 16 }
  return {}
}
