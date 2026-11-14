import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { Plus, Trash2 } from 'lucide-react'
import { Marker } from 'react-leaflet'
import type { LatLngBoundsLiteral } from 'leaflet'
import { problemMessage, useDeleteFarm, useFarms, type Farm } from '@/features/farms/api'
import { formatCoordinates, pointLatLng } from '@/features/farms/geo'
import { BaseMap } from '@/features/map/BaseMap'
import { homeIcon } from '@/features/map/markers'
import { TractorLoader, TractorSpinner } from '@/shared/ui/Tractor'

export function FarmsPage() {
  const navigate = useNavigate()
  const { data: farms, isPending, error, refetch } = useFarms()
  const remove = useDeleteFarm()
  const [confirmingId, setConfirmingId] = useState<string | null>(null)
  const [notice, setNotice] = useState('')
  if (isPending) return <div className="atlas-state"><TractorLoader /><p>Loading your farms…</p></div>
  if (error) return <div className="atlas-state"><h1>Your farms didn’t load.</h1><p>{problemMessage(error)}</p>
    <button className="button button-dark" onClick={() => void refetch()}>Try again</button></div>

  if (farms.length === 0) return <>
    <div className="workspace-heading"><h1>Put your farm on the map.</h1>
      <p>Start with a name and the point where the farm is. Then outline each field around it.</p></div>
    <section className="farms-empty">
      <img src="/images/contours.webp" alt="" />
      <div><h2>No farms yet.</h2><p>Your first farm takes a minute: a name and one click on the map.</p>
        <Link className="button button-dark" to="/dashboard/farms/new"><Plus size={17} />Create farm</Link></div>
    </section>
  </>

  async function confirmDelete(farm: Farm) {
    try {
      await remove.mutateAsync(farm.id)
      setConfirmingId(null)
      setNotice(`Deleted ${farm.name}.`)
    } catch { /* remove.error shows the reason in the confirmation */ }
  }

  const located = farms.filter(farm => farm.homePoint)
  const points = located.map(farm => pointLatLng(farm.homePoint!))
  const bounds: LatLngBoundsLiteral | undefined = points.length
    ? [[Math.min(...points.map(([lat]) => lat)), Math.min(...points.map(([, lng]) => lng))], [Math.max(...points.map(([lat]) => lat)), Math.max(...points.map(([, lng]) => lng))]]
    : undefined

  return <>
    <div className="workspace-heading farms-heading">
      <div><h1>Your farms</h1><p>Open a farm to draw, rename and edit its fields.</p></div>
      <Link className="button button-dark" to="/dashboard/farms/new"><Plus size={17} />Create farm</Link>
    </div>
    <div className="farms-overview">
      <ul className="farm-cards">{notice && <li className="farm-notice" role="status">{notice}</li>}{farms.map(farm => <li key={farm.id}>
        <Link to={`/dashboard/farms/${farm.id}`} className="farm-card">
          <strong>{farm.name}</strong>
          {farm.location && <span>{farm.location}</span>}
          <span className="mono">{farm.homePoint ? formatCoordinates(farm.homePoint) : 'No farm point yet'}</span>
        </Link>
        {confirmingId === farm.id
          ? <div className="farm-confirm" role="alertdialog" aria-labelledby={`delete-farm-${farm.id}`} aria-describedby={`delete-farm-${farm.id}-detail`}>
              <p id={`delete-farm-${farm.id}`}><strong>Delete {farm.name}?</strong></p>
              <p id={`delete-farm-${farm.id}-detail`}>Its fields, subfields and crop history, budget, machinery, operator hours, workers and expenses are deleted with it.
                This can’t be undone. The crop catalog and other farms are not affected.</p>
              {remove.error && <p className="field-error" role="alert">{problemMessage(remove.error)}</p>}
              <div className="field-actions">
                <button type="button" className="button button-danger" disabled={remove.isPending} onClick={() => void confirmDelete(farm)}>
                  {remove.isPending ? <TractorSpinner size={15} /> : <Trash2 size={15} />}Delete farm</button>
                <button type="button" className="button button-quiet" autoFocus disabled={remove.isPending} onClick={() => setConfirmingId(null)}>Keep farm</button>
              </div>
            </div>
          : <button type="button" className="button button-quiet button-icon farm-card-delete" aria-label={`Delete ${farm.name}`} title="Delete farm"
              disabled={remove.isPending} onClick={() => { remove.reset(); setNotice(''); setConfirmingId(farm.id) }}><Trash2 size={15} /></button>}
      </li>)}</ul>
      <BaseMap className="farms-map" label="Map of your farms" bounds={bounds && points.length > 1 ? bounds : undefined}
        center={points[0]} zoom={points.length === 1 ? 13 : 6}>
        {located.map(farm => <Marker key={farm.id} position={pointLatLng(farm.homePoint!)} icon={homeIcon({ label: farm.name })}
          title={`Open ${farm.name}`} eventHandlers={{ click: () => navigate(`/dashboard/farms/${farm.id}`) }} />)}
      </BaseMap>
    </div>
  </>
}
