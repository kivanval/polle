import { describe, expect, it } from 'vitest'
import type { CatalogColumn } from './api'
import { cell } from './format'

const column = (kind: CatalogColumn['kind']): CatalogColumn => ({
  name: 'allowed_soil_textures', kind, dbType: kind === 'json' ? 'jsonb' : '_text', nullable: true, hasDefault: false, generated: false,
  maxLength: null, options: null, link: null, key: false,
})

describe('cell', () => {
  it('shows a JSON list as readable words clipped to its column', () => {
    expect(cell(column('json'), ['sand', 'loamy_sand', 'sandy_loam'])).toEqual({ text: 'sand, loamy_sand, sandy_loam', muted: true, clip: true })
  })

  it('clips a Postgres array column the same way', () => {
    expect(cell(column('unsupported'), ['well', 'moderately_well'])).toMatchObject({ text: 'well, moderately_well', clip: true })
  })

  it('keeps JSON objects as JSON, clipped', () => {
    expect(cell(column('json'), { min: 1 })).toMatchObject({ text: '{"min":1}', clip: true })
  })

  it('clips long text with no spaces to break on', () => {
    expect(cell(column('text'), 'sand,loamy_sand,sandy_loam,loam,silt_loam,silt').clip).toBe(true)
    expect(cell(column('text'), 'Deep, well drained loam').clip).toBeUndefined()
  })
})
