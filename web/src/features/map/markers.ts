import { divIcon } from 'leaflet'

// Lucide "house" path, inlined because divIcon takes an HTML string.
const house = '<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M15 21v-8a1 1 0 0 0-1-1h-4a1 1 0 0 0-1 1v8"/><path d="M3 10a2 2 0 0 1 .709-1.528l7-5.999a2 2 0 0 1 2.582 0l7 5.999A2 2 0 0 1 21 10v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/></svg>'

export const homeIcon = (options: { moving?: boolean; label?: string } = {}) => divIcon({
  className: `home-pin ${options.moving ? 'is-moving' : ''}`,
  html: `<span class="home-pin-halo"></span><span class="home-pin-core">${house}</span>${options.label ? `<span class="home-pin-label">${escapeHtml(options.label)}</span>` : ''}`,
  iconSize: [40, 40],
  iconAnchor: [20, 20],
})

/** Expanding rings at a newly claimed field's centre. */
export const claimRipple = divIcon({
  className: 'claim-ripple',
  html: '<span></span><span></span><span></span>',
  iconSize: [24, 24],
  iconAnchor: [12, 12],
})

function escapeHtml(value: string) {
  return value.replace(/[&<>"']/g, char => `&#${char.charCodeAt(0)};`)
}
