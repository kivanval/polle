import { describe, expect, it } from 'vitest'
import { colorMapUrl, gibsText, parseColorMap, tilePixel } from './layerPoint'
import type { SatelliteLayer } from './api'

const xml = `<ColorMaps>
  <ColorMap title="No Data"><Entries><ColorMapEntry rgb="64,64,64" transparent="true" sourceValue="[0,1)" nodata="true" ref="0"/></Entries></ColorMap>
  <ColorMap title="Land Surface Temperature" units="K"><Entries>
    <ColorMapEntry rgb="197,0,255" transparent="false" sourceValue="[10000,10030)" value="[200.00,200.60)" ref="2"/>
    <ColorMapEntry rgb="0,255,255" transparent="false" sourceValue="[12940,12970)" value="[258.80,259.40)" ref="100"/>
  </Entries></ColorMap></ColorMaps>`

describe('parseColorMap', () => {
  it('reads each colour with its value range and the units', () => {
    const map = parseColorMap(xml)
    expect(map.units).toBe('K')
    expect(map.entries).toHaveLength(3)
    expect(map.entries[0].nodata).toBe(true)
    expect(map.entries[2]).toEqual({ rgb: [0, 255, 255], from: 258.8, to: 259.4, nodata: false })
  })
})

describe('colorMapUrl', () => {
  it('names the colour map after the legend', () => {
    expect(colorMapUrl({ legend_url: 'https://gibs.earthdata.nasa.gov/legends/MODIS_NDVI_H.svg' } as SatelliteLayer))
      .toBe('https://gibs.earthdata.nasa.gov/colormaps/v1.3/MODIS_NDVI.xml')
  })
})

describe('tilePixel', () => {
  it('finds the Web Mercator tile and pixel of a place', () => {
    expect(tilePixel(0, 0, 1)).toEqual({ column: 1, row: 1, x: 0, y: 0 })
    const lviv = tilePixel(49.84, 24.03, 6)
    expect(lviv.column).toBe(36)
    expect(lviv.row).toBe(21)
  })
})

describe('gibsText', () => {
  it('turns kelvin into °C, soil moisture into percent and NDVI into words', () => {
    expect(gibsText({ value: 293.15, units: 'K', color: '#fff', date: null }, 'surface_temperature').value).toBe('20 °C')
    expect(gibsText({ value: 0.274, units: 'm³/m³', color: '#fff', date: null }, 'soil_moisture').value).toBe('27%')
    const ndvi = gibsText({ value: 0.72, units: '', color: '#fff', date: null }, 'ndvi')
    expect(ndvi.value).toBe('0.72')
    expect(ndvi.meaning).toMatch(/Dense/)
  })
})
