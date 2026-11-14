import { describe, expect, it } from 'vitest'
import { idPlaceholder, pageTitle } from './pageTitle'

const farmPath = '/dashboard/farms/c3f35d15-a17b-4f05-a13f-2c2238dea48b'

describe('pageTitle', () => {
  it('names a farm page after the farm, never its id', () => {
    expect(pageTitle(farmPath, 'Tester')).toBe('Polle — Tester')
  })

  it('shows "Farm" while the farm loads instead of the raw UUID', () => {
    const title = pageTitle(farmPath, null)
    expect(title).toBe('Polle — Farm')
    expect(title).not.toMatch(/[0-9a-f]{8}-[0-9a-f]{4}/i)
  })

  it('treats an id in any letter case as an id', () => {
    expect(pageTitle(farmPath.toUpperCase().replace('/DASHBOARD/FARMS/', '/dashboard/farms/'), null)).toBe('Polle — Farm')
  })

  it('keeps word segments as page names and the landing title for the root', () => {
    expect(pageTitle('/dashboard/climate', null)).toBe('Polle — Climate')
    expect(pageTitle('/dashboard/farms/planning', null)).toBe('Polle — Planning')
    expect(pageTitle('/', 'Ignored')).toBe('Polle — Earth intelligence, rooted in your land')
  })

  it('ignores a blank published name', () => {
    expect(pageTitle(farmPath, '   ')).toBe('Polle — Farm')
  })
})

describe('idPlaceholder', () => {
  it('names what an id under a section is, and nothing for page names', () => {
    expect(idPlaceholder(farmPath)).toBe('Farm')
    expect(idPlaceholder('/dashboard/farms/new')).toBeNull()
    expect(idPlaceholder('/dashboard/farms/fields')).toBeNull()
    expect(idPlaceholder('/dashboard/things/42')).toBe('Workspace')
  })
})
