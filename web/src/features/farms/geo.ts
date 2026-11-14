import area from '@turf/area'
import centroid from '@turf/centroid'
import { featureCollection, polygon } from '@turf/helpers'
import intersect from '@turf/intersect'
import kinks from '@turf/kinks'
import type { Feature, MultiPolygon, Polygon } from 'geojson'
import type { LatLngExpression, LatLngLiteral, LatLngTuple } from 'leaflet'
import type { Field, PointGeometry, PolygonGeometry } from './api'

/** Matches the API: fields may share an edge; intersections up to 1 m² count as touching. */
const OVERLAP_TOLERANCE_M2 = 1
export const MAX_VERTICES = 500

// 7 decimals ≈ 1 cm. Keeps request bodies well under the API's 16 KiB limit.
const round = (value: number) => Math.round(value * 1e7) / 1e7

export function toPolygon(latLngs: LatLngLiteral[]): PolygonGeometry {
  const ring = latLngs
    .map(({ lat, lng }) => [round(lng), round(lat)] as [number, number])
    .filter((point, index, points) => index === 0 || point[0] !== points[index - 1][0] || point[1] !== points[index - 1][1])
  const [first, last] = [ring[0], ring.at(-1)]
  if (first && last && first[0] === last[0] && first[1] === last[1]) ring.pop()
  return { type: 'Polygon', coordinates: [[...ring, ring[0]]] }
}

export const toLatLngs = (geometry: PolygonGeometry): LatLngTuple[] =>
  geometry.coordinates[0].slice(0, -1).map(([lng, lat]) => [lat, lng])

export const toPoint = ({ lat, lng }: LatLngLiteral): PointGeometry => ({ type: 'Point', coordinates: [round(lng), round(lat)] })
export const pointLatLng = (point: PointGeometry): LatLngTuple => [point.coordinates[1], point.coordinates[0]]

export const crossesItself = (geometry: PolygonGeometry) => kinks(polygon(geometry.coordinates)).features.length > 0
export const vertexCount = (geometry: PolygonGeometry) => geometry.coordinates[0].length - 1
export const hectares = (geometry: PolygonGeometry) => area(polygon(geometry.coordinates)) / 10_000

export function centre(geometry: PolygonGeometry): LatLngExpression {
  const [lng, lat] = centroid(polygon(geometry.coordinates)).geometry.coordinates
  return [lat, lng]
}

export type Overlap = { field: Field; shape: Feature<Polygon | MultiPolygon> }

export function findOverlap(candidate: PolygonGeometry, fields: Field[], ignoreFieldId?: string): Overlap | null {
  const drawn = polygon(candidate.coordinates)
  for (const field of fields) {
    if (field.id === ignoreFieldId) continue
    const shape = intersect(featureCollection([drawn, polygon(field.boundary.coordinates)]))
    if (shape && area(shape) > OVERLAP_TOLERANCE_M2) return { field, shape }
  }
  return null
}

/** Whether a subfield outline lies inside its parent, with the API's 1 m² tolerance. */
export function liesInside(candidate: PolygonGeometry, parent: PolygonGeometry): boolean {
  const drawn = polygon(candidate.coordinates)
  const shared = intersect(featureCollection([drawn, polygon(parent.coordinates)]))
  return Boolean(shared) && area(drawn) - area(shared!) <= OVERLAP_TOLERANCE_M2
}

/**
 * Fields a boundary must not overlap: every other field, except that a subfield may overlap its own parent and a
 * parent its own subfields.
 */
export const overlapCandidates = (fields: Field[], parentId: string | null, selfId?: string) =>
  fields.filter(field => field.id !== parentId && (!selfId || field.parentFieldId !== selfId))

export const formatHectares = (value: number) =>
  `${value.toLocaleString('en', { maximumFractionDigits: value < 10 ? 2 : 1, minimumFractionDigits: value < 10 ? 2 : 1 })} ha`

export const formatCoordinates = (point: PointGeometry) =>
  `${Math.abs(point.coordinates[1]).toFixed(5)}° ${point.coordinates[1] >= 0 ? 'N' : 'S'}, ${Math.abs(point.coordinates[0]).toFixed(5)}° ${point.coordinates[0] >= 0 ? 'E' : 'W'}`
