import { useState, type FormEvent } from 'react'
import { useMap } from 'react-leaflet'
import { LocateFixed, Search } from 'lucide-react'
import type { LatLngLiteral } from 'leaflet'
import { MapControl } from './BaseMap'
import { TractorSpinner } from '@/shared/ui/Tractor'

type Place = { place_id: number; display_name: string; lat: string; lon: string; boundingbox: [string, string, string, string] }

/**
 * Find a village or address and fly there. Nominatim's usage policy allows occasional searches,
 * not search-as-you-type, so this only queries on submit.
 */
export function PlaceSearch({ onLocated }: { onLocated?: (position: LatLngLiteral) => void }) {
  const map = useMap()
  const [query, setQuery] = useState('')
  const [places, setPlaces] = useState<Place[] | null>(null)
  const [status, setStatus] = useState<'idle' | 'searching' | 'locating'>('idle')
  const [message, setMessage] = useState('')

  async function search(event: FormEvent) {
    event.preventDefault()
    if (!query.trim()) return
    setStatus('searching'); setMessage('')
    try {
      const params = new URLSearchParams({ q: query.trim(), format: 'jsonv2', limit: '5' })
      const response = await fetch(`https://nominatim.openstreetmap.org/search?${params}`, { headers: { 'Accept-Language': 'en' } })
      const results = await response.json() as Place[]
      setPlaces(results)
      if (!results.length) setMessage('No places match. Try a village, district or region name.')
    } catch { setMessage('Place search is unavailable. Pan the map to your land instead.') }
    finally { setStatus('idle') }
  }

  function go(place: Place) {
    const [south, north, west, east] = place.boundingbox.map(Number)
    map.flyToBounds([[south, west], [north, east]], { maxZoom: 16, duration: 1.2 })
    setPlaces(null); setQuery(place.display_name.split(',')[0])
  }

  function locate() {
    if (!navigator.geolocation) { setMessage('This browser can’t share your location.'); return }
    setStatus('locating'); setMessage('')
    navigator.geolocation.getCurrentPosition(({ coords }) => {
      const position = { lat: coords.latitude, lng: coords.longitude }
      map.flyTo(position, 16, { duration: 1.2 })
      onLocated?.(position)
      setStatus('idle')
    }, () => { setMessage('Location access was blocked. Search for a place instead.'); setStatus('idle') }, { timeout: 10_000 })
  }

  return <MapControl className="place-search">
    <form role="search" onSubmit={search}>
      <Search size={16} aria-hidden="true" />
      <input value={query} onChange={event => { setQuery(event.target.value); setPlaces(null) }} placeholder="Search a village or address"
        aria-label="Search for a place" autoComplete="off" />
      {status === 'searching' && <TractorSpinner size={16} label="Searching" />}
      <button type="button" className="place-locate" onClick={locate} aria-label="Go to my location" title="Go to my location" disabled={status === 'locating'}>
        {status === 'locating' ? <TractorSpinner size={16} /> : <LocateFixed size={16} />}
      </button>
    </form>
    {places && places.length > 0 && <ul className="place-results">{places.map(place => <li key={place.place_id}>
      <button type="button" onClick={() => go(place)}>{place.display_name}</button></li>)}</ul>}
    {message && <p className="place-message" role="status">{message}</p>}
  </MapControl>
}
