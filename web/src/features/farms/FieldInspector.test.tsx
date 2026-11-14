import { renderToStaticMarkup } from 'react-dom/server'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import type { Field, PolygonGeometry } from './api'
import { FieldInspector, type FieldInspectorProps } from './FieldInspector'
import { FieldList } from './FieldList'

const boundary: PolygonGeometry = { type: 'Polygon', coordinates: [[[30, 50], [30.01, 50], [30.01, 50.01], [30, 50.01], [30, 50]]] }
const field = (id: string, name: string, parentFieldId: string | null = null, areaHectares = 12): Field => ({
  id, farmId: 'farm', name, description: null, parentFieldId, areaHectares, boundary, createdAtUtc: '', updatedAtUtc: '', soilAvailable: true,
})
const north = field('north', 'North field'), part = field('part', 'North field · A', 'north', 3)
const noop = () => {}
const inspector = (overrides: Partial<FieldInspectorProps> = {}) => renderToStaticMarkup(<MemoryRouter>
  <FieldInspector field={north} fields={[north, part]} plantings={[]} crops={[]} busy={false} error=""
    onClose={noop} onSelect={noop} onRename={async () => true} onEditBoundary={noop} onDelete={async () => ''} onAddSubfield={noop}
    onSaveConditions={async () => true} {...overrides} />
</MemoryRouter>)
const text = (html: string) => html.replace(/<[^>]+>/g, ' ').replace(/&amp;/g, '&').replace(/\s+/g, ' ')

describe('FieldInspector', () => {
  it('puts every action of the selected field in one toolbar, in order', () => {
    const html = inspector()
    const toolbar = text(html.match(/role="toolbar"[^>]*>(.*?)<\/div>/)?.[1] ?? '')
    const order = ['Rename', 'Boundary', 'Soil & climate', 'Subfield', 'Delete'].map(label => toolbar.indexOf(label))
    expect(order.every(index => index >= 0)).toBe(true)
    expect([...order].sort((a, b) => a - b)).toEqual(order)
    expect(html).toContain('aria-label="Delete North field"')
  })

  it('names the field, its area and how to close it, with Plan & strategies at the foot', () => {
    const html = inspector()
    expect(html).toContain('id="field-inspector"')
    expect(html).toContain('>North field</h2>')
    expect(html).toContain('aria-label="Close field"')
    expect(text(html)).toMatch(/Area\s+12(\.0)? ha/)
    expect(html.indexOf('inspector-foot')).toBeGreaterThan(html.indexOf('inspector-toolbar'))
    expect(text(html)).toContain('Plan & strategies')
  })

  it('offers no subfield of a subfield and links back to its field', () => {
    const html = inspector({ field: part })
    expect(text(html)).not.toMatch(/\bSubfield\b(?! of)/)
    expect(text(html)).toContain('Subfield of North field')
  })

  it('disables every action while the farm is saving', () => {
    const html = inspector({ busy: true })
    const tools = html.match(/<button[^>]*class="inspector-tool(?: is-danger)?"[^>]*>/g) ?? []
    expect(tools.length).toBeGreaterThanOrEqual(5)
    expect(tools.every(tag => tag.includes('disabled'))).toBe(true)
  })
})

describe('FieldList', () => {
  it('keeps rows compact: no field actions inside the list', () => {
    const html = renderToStaticMarkup(<FieldList fields={[north, part]} plantings={[]} crops={[]} selectedId="north" hoveredId={null} savedId={null}
      busy={false} onSelect={noop} onHover={noop} />)
    expect(html).toContain('North field')
    expect(html).toContain('aria-controls="field-inspector"')
    for (const action of ['Rename', 'Edit Boundary', 'Soil &amp; climate', 'Add subfield', 'Plan &amp; strategies']) expect(html).not.toContain(action)
  })
})
