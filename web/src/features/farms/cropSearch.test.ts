import { describe, expect, it } from 'vitest'
import { cropMeta, searchCrops } from './cropSearch'
import type { Crop } from './plantings'

const profiles = (count: number) => Array.from({ length: count }, (_, index) => ({ id: index + 1, systemName: `System ${index}`, isPerennial: false }))
const crop = (id: number, name: string, nameUk: string | null, scientificName: string | null, profileCount = 1): Crop =>
  ({ id, name, nameUk, scientificName, profiles: profiles(profileCount) })

const catalog: Crop[] = [
  crop(1, 'Abaca', null, 'Musa textilis'),
  crop(2, 'Chickpea', 'Chickpea (uk)', 'Cicer arietinum', 3),
  crop(3, 'Corn', 'Maize (uk)', 'Zea mays', 5),
  crop(4, 'Peas', 'Field pea (uk)', 'Pisum sativum', 7),
  crop(5, 'Wheat', 'Soft wheat (uk)', 'Triticum aestivum', 5),
]

describe('searchCrops', () => {
  it('matches English, local and Latin names', () => {
    expect(searchCrops(catalog, 'soft').map(found => found.name)).toEqual(['Wheat'])
    expect(searchCrops(catalog, 'zea').map(found => found.name)).toEqual(['Corn'])
    expect(searchCrops(catalog, 'WHEAT').map(found => found.name)).toEqual(['Wheat'])
  })

  it('ranks prefix matches first', () => {
    expect(searchCrops(catalog, 'pea').map(found => found.name)).toEqual(['Peas', 'Chickpea'])
  })

  it('lists crops with several profiles first for an empty query', () => {
    expect(searchCrops(catalog, '  ').map(found => found.name)).toEqual(['Peas', 'Corn', 'Wheat', 'Chickpea', 'Abaca'])
  })

  it('caps the result list', () => {
    expect(searchCrops(catalog, '', 2)).toHaveLength(2)
  })
})

describe('cropMeta', () => {
  it('shows only the Latin name, never the local one', () => {
    expect(cropMeta(crop(5, 'Wheat', 'Soft wheat (uk)', 'Triticum aestivum'))).toBe('Triticum aestivum')
    expect(cropMeta(crop(6, 'Abaca', 'Abaca (uk)', null))).toBe('')
  })
})
