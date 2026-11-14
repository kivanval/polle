import { Link } from 'react-router'

export function Brand({ light = false }: { light?: boolean }) {
  return <Link className={`brand ${light ? 'brand-light' : ''}`} to="/" aria-label="Polle home">
    <svg viewBox="0 0 32 32" fill="none" aria-hidden="true"><path d="M16 28V15M16 21C6 21 3 14 3 7c8 0 13 4 13 14ZM16 17C16 7 21 3 29 3c0 9-4 14-13 14Z" stroke="currentColor" strokeWidth="1.8" strokeLinejoin="round" /></svg>
    Polle
  </Link>
}
