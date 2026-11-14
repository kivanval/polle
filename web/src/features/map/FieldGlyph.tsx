import type { PolygonGeometry } from '@/features/farms/api'

/** Small silhouette of a field boundary, so list rows are recognisable by shape. */
export function FieldGlyph({ boundary, size = 36 }: { boundary: PolygonGeometry; size?: number }) {
  const ring = boundary.coordinates[0]
  const latitude = ring.reduce((sum, [, lat]) => sum + lat, 0) / ring.length
  // Scale longitude by cos(latitude) so shapes are not stretched east–west.
  const points = ring.map(([lng, lat]) => [lng * Math.cos(latitude * Math.PI / 180), -lat])
  const xs = points.map(([x]) => x), ys = points.map(([, y]) => y)
  const [minX, minY] = [Math.min(...xs), Math.min(...ys)]
  const span = Math.max(Math.max(...xs) - minX, Math.max(...ys) - minY) || 1
  const offsetX = (span - (Math.max(...xs) - minX)) / 2, offsetY = (span - (Math.max(...ys) - minY)) / 2
  const path = points.map(([x, y], index) =>
    `${index ? 'L' : 'M'}${(4 + (x - minX + offsetX) / span * 92).toFixed(1)} ${(4 + (y - minY + offsetY) / span * 92).toFixed(1)}`).join(' ')
  return <svg className="field-glyph" viewBox="0 0 100 100" width={size} height={size} aria-hidden="true">
    <path d={`${path} Z`} vectorEffect="non-scaling-stroke" />
  </svg>
}
