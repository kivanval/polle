import { Link } from 'react-router'
import { Brand } from '@/shared/ui/Brand'
export function NotFoundPage() {
  return <main className="route-state"><Brand /><span className="mono">404 / UNCHARTED TERRITORY</span><h1>A little off the map.</h1><p>This page doesn’t exist. Let’s get you back to familiar ground.</p><Link to="/" className="button button-dark">Back to home</Link></main>
}
