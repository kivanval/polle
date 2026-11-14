import { useLayoutEffect, useState, type RefObject } from 'react'
import { fieldBoundaries, imageHeight, imageWidth } from '@/shared/ui/fieldBoundaries'

type Rect = { left: number; top: number; right: number; bottom: number }
type Point = [number, number]
export type HeroFraming = { viewBox: string; fields: string[] }

/** Art-directed order: the most legible, characteristic fields first. */
const priority = ['long-rows', 'centre-green', 'river-rows', 'north-stubble', 'east-rows', 'south-stubble', 'meadow']
/** Fields whose strokes sit close enough to read as one dense cluster; never shown together. */
const neighbours = [['river-rows', 'meadow'], ['meadow', 'long-rows'], ['east-rows', 'meadow'], ['east-rows', 'long-rows'],
  ['centre-green', 'south-stubble'], ['north-stubble', 'river-rows']]
const maxFields = 3
const clearance = 20
/** How far (image px) the cover crop may move from centre along its free axis to keep fields whole. */
const maxShift = 240

const isNeighbour = (a: string, b: string) => neighbours.some(([x, y]) => (x === a && y === b) || (x === b && y === a))
const inflate = (rect: DOMRect | Rect, by: number): Rect => ({ left: rect.left - by, top: rect.top - by, right: rect.right + by, bottom: rect.bottom + by })

const bounds = (polygon: Point[]): Rect => ({ left: Math.min(...polygon.map(([x]) => x)), right: Math.max(...polygon.map(([x]) => x)),
  top: Math.min(...polygon.map(([, y]) => y)), bottom: Math.max(...polygon.map(([, y]) => y)) })
// Bounding boxes are deliberately conservative: an outline must keep clear of every text line, not merely avoid touching it.
const overlaps = (a: Rect, b: Rect) => a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom

/** Rendered line boxes of an element's text, excluding the boxes of block-level wrappers. */
function glyphRects(element: Element): DOMRect[] {
  const rects: DOMRect[] = []
  const range = document.createRange()
  const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT)
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    range.selectNodeContents(node)
    rects.push(...[...range.getClientRects()].filter(rect => rect.width > 0))
  }
  return rects
}

/** Line boxes of the hero copy. Headline spans animate in, so their height comes from the static clipping parent. */
function textRects(hero: HTMLElement, origin: DOMRect): Rect[] {
  const rects: Rect[] = []
  const relative = (rect: Rect) => ({ left: rect.left - origin.left, top: rect.top - origin.top, right: rect.right - origin.left, bottom: rect.bottom - origin.top })
  hero.querySelectorAll<HTMLElement>('.headline-line').forEach(line => {
    const glyphs = glyphRects(line)
    if (glyphs.length === 0) { return }
    const box = line.getBoundingClientRect()
    rects.push(relative({ left: Math.min(...glyphs.map(r => r.left)), right: Math.max(...glyphs.map(r => r.right)), top: box.top, bottom: box.bottom }))
  })
  hero.querySelectorAll<HTMLElement>('.hero-description').forEach(text => glyphRects(text).forEach(line => rects.push(relative(line))))
  hero.querySelectorAll<HTMLElement>('.hero-actions > *').forEach(action => rects.push(relative(action.getBoundingClientRect())))
  return rects.map(rect => inflate(rect, clearance))
}

function frame(hero: HTMLElement): HeroFraming {
  const origin = hero.getBoundingClientRect()
  const width = origin.width, height = origin.height
  const header = hero.querySelector('.site-header')?.getBoundingClientRect()
  const foot = hero.querySelector('.hero-foot')?.getBoundingClientRect()
  const safe = { left: clearance, right: width - clearance,
    top: (header ? header.bottom - origin.top : 0) + clearance, bottom: (foot ? foot.top - origin.top : height) - clearance }
  const copy = textRects(hero, origin)
  // Same scale as a centred cover; only the crop position along the free axis may vary.
  const scale = Math.max(width / imageWidth, height / imageHeight)
  const viewWidth = width / scale, viewHeight = height / scale
  const horizontal = imageWidth - viewWidth > imageHeight - viewHeight
  const slack = horizontal ? imageWidth - viewWidth : imageHeight - viewHeight

  let best: { fields: string[]; score: number; shift: number; x: number; y: number } | undefined
  for (let shift = -maxShift; shift <= maxShift; shift += 20) {
    const offset = Math.min(Math.max(slack / 2 + shift, 0), slack)
    const x = horizontal ? offset : (imageWidth - viewWidth) / 2
    const y = horizontal ? (imageHeight - viewHeight) / 2 : offset
    const fields: string[] = []
    for (const id of priority) {
      if (fields.length === maxFields || fields.some(other => isNeighbour(id, other))) { continue }
      const box = bounds(fieldBoundaries.find(field => field.id === id)!.points.map(([px, py]): Point => [(px - x) * scale, (py - y) * scale]))
      const framed = box.left >= safe.left && box.right <= safe.right && box.top >= safe.top && box.bottom <= safe.bottom
      if (framed && !copy.some(rect => overlaps(box, rect))) { fields.push(id) }
    }
    const score = fields.reduce((sum, id) => sum + priority.length - priority.indexOf(id), 0)
    if (!best || score > best.score || (score === best.score && Math.abs(shift) < Math.abs(best.shift))) {
      best = { fields, score, shift, x, y }
    }
  }
  const round = (value: number) => Math.round(value * 10) / 10
  return { viewBox: `${round(best!.x)} ${round(best!.y)} ${round(viewWidth)} ${round(viewHeight)}`, fields: best!.fields }
}

/** Picks the crop and the few hero outlines that sit whole and clear of the copy at the current viewport. */
export function useHeroFraming(hero: RefObject<HTMLElement | null>) {
  const [framing, setFraming] = useState<HeroFraming>()
  useLayoutEffect(() => {
    const element = hero.current
    if (!element) { return }
    let frameId = 0
    const update = () => {
      const next = frame(element)
      setFraming(current => current?.viewBox === next.viewBox && current.fields.join() === next.fields.join() ? current : next)
    }
    update()
    const observer = new ResizeObserver(() => { cancelAnimationFrame(frameId); frameId = requestAnimationFrame(update) })
    observer.observe(element)
    let active = true
    document.fonts?.ready.then(() => { if (active) { update() } })
    return () => { active = false; observer.disconnect(); cancelAnimationFrame(frameId) }
  }, [hero])
  return framing
}
