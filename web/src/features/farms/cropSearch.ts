import type { Crop } from './plantings'

/**
 * Crops matching a typed query by English, Ukrainian or Latin name. Prefix matches rank first, then crops with more
 * growing profiles (the well-described field crops), then by name. An empty query lists those field crops first.
 */
export function searchCrops(crops: Crop[], query: string, limit = 30): Crop[] {
  const needle = query.trim().toLocaleLowerCase()
  const names = (crop: Crop) => [crop.name, crop.nameUk, crop.scientificName].flatMap(name => name ? [name.toLocaleLowerCase()] : [])
  return crops
    .flatMap(crop => {
      if (!needle) return [{ crop, prefix: false }]
      const matches = names(crop)
      if (!matches.some(name => name.includes(needle))) return []
      return [{ crop, prefix: matches.some(name => name.startsWith(needle)) }]
    })
    .sort((a, b) => Number(b.prefix) - Number(a.prefix) || b.crop.profiles.length - a.crop.profiles.length || a.crop.name.localeCompare(b.crop.name))
    .slice(0, limit)
    .map(({ crop }) => crop)
}

/** The line under a crop's name in the picker. The UI is English-only, so the Ukrainian name is searchable but never shown. */
export const cropMeta = (crop: Crop) => crop.scientificName ?? ''
