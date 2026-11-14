import { afterEach, describe, expect, it, vi } from 'vitest'
import { placeLabel, reverseGeocode } from './geocode'

describe('placeLabel', () => {
  it('names the nearest settlement, its region and country', () => {
    expect(placeLabel({ village: 'Reshetylivka', county: 'Poltava Raion', state: 'Poltava Oblast', country: 'Ukraine' }))
      .toBe('Reshetylivka, Poltava Oblast, Ukraine')
  })

  it('falls back to a town or city, then the county when there is no state', () => {
    expect(placeLabel({ town: 'Bila Tserkva', county: 'Kyiv Oblast', country: 'Ukraine' })).toBe('Bila Tserkva, Kyiv Oblast, Ukraine')
    expect(placeLabel({ state: 'Kyiv', city: 'Kyiv', country: 'Ukraine' })).toBe('Kyiv, Ukraine')
  })

  it('is null without an address', () => {
    expect(placeLabel(undefined)).toBeNull()
    expect(placeLabel({})).toBeNull()
  })
})

describe('reverseGeocode', () => {
  afterEach(() => { vi.unstubAllGlobals() })

  it('asks Nominatim for the point and reads its address', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ address: { village: 'Mala Pereshchepyna', state: 'Poltava Oblast', country: 'Ukraine' } })))
    vi.stubGlobal('fetch', fetch)
    await expect(reverseGeocode({ lat: 49.0123456, lng: 34.5 })).resolves.toBe('Mala Pereshchepyna, Poltava Oblast, Ukraine')
    const url = new URL(fetch.mock.calls[0][0] as string)
    expect(url.origin + url.pathname).toBe('https://nominatim.openstreetmap.org/reverse')
    expect(url.searchParams.get('lat')).toBe('49.01235')
    expect(url.searchParams.get('lon')).toBe('34.50000')
  })

  it('is null where there is no address or the service refuses', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ error: 'Unable to geocode' }))))
    await expect(reverseGeocode({ lat: 0, lng: -30 })).resolves.toBeNull()
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('busy', { status: 429 })))
    await expect(reverseGeocode({ lat: 0, lng: -30 })).resolves.toBeNull()
  })
})
