import { useEffect, useState } from 'react'
import type { LatLngLiteral } from 'leaflet'

/** The parts of a Nominatim address this app reads; any of them may be missing. */
export type NominatimAddress = Partial<Record<
  'hamlet' | 'village' | 'town' | 'city' | 'municipality' | 'suburb' | 'county' | 'district' | 'state' | 'region' | 'country', string>>

/**
 * "Reshetylivka, Poltava Oblast, Ukraine": the nearest settlement, its region and country, without repeats. Null when
 * the point has no address (open sea).
 */
export function placeLabel(address: NominatimAddress | undefined | null): string | null {
  if (!address) return null
  const settlement = address.village ?? address.town ?? address.city ?? address.hamlet ?? address.municipality ?? address.suburb
  const region = address.state ?? address.region ?? address.county ?? address.district
  const parts = [settlement, region, address.country].filter((part): part is string => Boolean(part?.trim()))
  const unique = parts.filter((part, index) => parts.indexOf(part) === index)
  return unique.length ? unique.join(', ').slice(0, 500) : null
}

/** The place name at a point from OpenStreetMap's Nominatim, or null when there is none or the lookup fails. */
export async function reverseGeocode(point: LatLngLiteral, signal?: AbortSignal): Promise<string | null> {
  const params = new URLSearchParams({ lat: point.lat.toFixed(5), lon: point.lng.toFixed(5), format: 'jsonv2', zoom: '14', addressdetails: '1' })
  const response = await fetch(`https://nominatim.openstreetmap.org/reverse?${params}`, { headers: { 'Accept-Language': 'en' }, signal })
  if (!response.ok) return null
  const body = await response.json() as { address?: NominatimAddress; error?: string }
  return body.error ? null : placeLabel(body.address)
}

export type PlaceLookup = { status: 'idle' | 'looking' | 'found' | 'none' | 'failed'; label: string | null }

/**
 * The place name at a point, looked up once the point stops moving (dragging a marker sends many points; Nominatim's
 * policy allows about one request a second). A new point cancels the lookup still running.
 */
export function usePlaceName(point: LatLngLiteral | null, delayMs = 700): PlaceLookup {
  const [lookup, setLookup] = useState<PlaceLookup & { key: string | null }>({ status: 'idle', label: null, key: null })
  const lat = point?.lat, lng = point?.lng
  const key = lat !== undefined && lng !== undefined ? `${lat.toFixed(5)},${lng.toFixed(5)}` : null
  useEffect(() => {
    if (lat === undefined || lng === undefined || !key) return
    const controller = new AbortController()
    const timer = setTimeout(() => {
      setLookup({ status: 'looking', label: null, key })
      reverseGeocode({ lat, lng }, controller.signal)
        .then(label => setLookup({ status: label ? 'found' : 'none', label, key }))
        .catch(() => { if (!controller.signal.aborted) setLookup({ status: 'failed', label: null, key }) })
    }, delayMs)
    return () => { clearTimeout(timer); controller.abort() }
    // Keyed by the coordinates, not the object: a new object at the same point needs no new lookup.
  }, [lat, lng, key, delayMs])
  if (!key) return { status: 'idle', label: null }
  // Until the lookup for this point starts, the last one's answer is stale.
  return lookup.key === key ? { status: lookup.status, label: lookup.label } : { status: 'looking', label: null }
}
