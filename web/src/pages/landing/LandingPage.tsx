import { useRef, useState } from 'react'
import { Link } from 'react-router'
import { motion, useInView, useReducedMotion, useScroll, useTransform } from 'motion/react'
import { ArrowDown, ArrowRight, ArrowUpRight, Globe2, Menu, X } from 'lucide-react'
import { Brand } from '@/shared/ui/Brand'
import { useAuth } from '@/features/auth/useAuth'
import { fieldLabel } from '@/shared/ui/fieldBoundaries'
import { FieldLines } from '@/shared/ui/FieldLines'
import { useHeroFraming } from './useHeroFraming'
import './landing.css'

const workflow = [
  { title: 'Start with your land', text: 'Create your farms, map field boundaries, and log your current crops.' },
  { title: 'Pull climate & soil data', text: 'Automatic 10-year climate and drought context from open Earth observation datasets, plus a soil estimate you can correct.' },
  { title: 'Plan the next season', text: 'Compare optimized crop rotation strategies grounded in your local field conditions.' },
]
const copyrightYear = new Date().getFullYear()

export function LandingPage() {
  const [menuOpen, setMenuOpen] = useState(false)
  const { data: user } = useAuth()
  const [boundaries, setBoundaries] = useState(true)
  const [selectedField, setSelectedField] = useState<string>()
  const atlas = useRef<HTMLDivElement>(null)
  // Outlines wait until the atlas is on screen so the first trace is seen rather than spent off-screen.
  const atlasSeen = useInView(atlas, { once: true, amount: .45 })
  const showBoundaries = boundaries && atlasSeen
  const hero = useRef<HTMLElement>(null)
  const heroFraming = useHeroFraming(hero)
  const reduced = useReducedMotion()
  const { scrollYProgress } = useScroll({ target: hero, offset: ['start start', 'end start'] })
  const imageY = useTransform(scrollYProgress, [0, 1], ['0%', '18%'])
  const imageScale = useTransform(scrollYProgress, [0, 1], [1, 1.08])
  return <>
    <a className="skip-link" href="#main">Skip to content</a>
    <section className="hero" ref={hero}>
      <motion.div className="hero-visual" style={reduced ? undefined : { y: imageY, scale: imageScale }}>
        {/* The satellite pass only runs when it has outlines to reveal; narrow crops leave the photograph untouched. */}
        <FieldLines className="hero-boundaries" scan={!!heroFraming?.fields.length} only={heroFraming?.fields ?? []} viewBox={heroFraming?.viewBox} />
      </motion.div>
      <header className="site-header wrap">
        <Brand light /><nav className="desktop-nav" aria-label="Main navigation"><a href="#platform">Platform</a><a href="#earth-intelligence">Earth intelligence</a><Link to="/climate">Climate map</Link><Link to="/sources">Sources</Link></nav>
        {/* A valid session is kept across visits to this page, so signed-in users go straight back to their workspace. */}
        <div className="header-actions">{user
          ? <Link className="button button-lime header-cta" to="/dashboard">Open workspace <ArrowUpRight size={16} /></Link>
          : <><Link className="header-login" to="/login">Log in</Link><Link className="button button-lime header-cta" to="/register">Get started <ArrowUpRight size={16} /></Link></>}
          <button className="menu-button" aria-label={menuOpen ? 'Close menu' : 'Open menu'} aria-expanded={menuOpen} aria-controls="mobile-nav" onClick={() => setMenuOpen(!menuOpen)}>{menuOpen ? <X /> : <Menu />}</button></div>
      </header>
      {menuOpen && <nav id="mobile-nav" className="mobile-nav" aria-label="Mobile navigation"><a href="#platform" onClick={() => setMenuOpen(false)}>Platform <ArrowUpRight /></a><a href="#earth-intelligence" onClick={() => setMenuOpen(false)}>Earth intelligence <ArrowUpRight /></a><Link to="/climate">Climate map <ArrowUpRight /></Link><Link to="/sources">Sources <ArrowUpRight /></Link>{user ? <Link to="/dashboard">Open workspace <ArrowRight /></Link> : <Link to="/login">Log in <ArrowRight /></Link>}</nav>}
      <main id="main" className="hero-content wrap">
        <h1><span className="headline-line"><span>A new perspective.</span></span><span className="headline-line accent"><span>A more resilient harvest.</span></span></h1>
        <p className="hero-description">Earth intelligence for the land you know.<br />A foundation for climate-aware farming,<br className="desktop-break" /> from the first field to the next season.</p>
        <div className="hero-actions"><Link className="button button-lime" to="/register">Start your journey <ArrowRight size={20} /></Link><a className="text-link light" href="#platform">Explore the platform <ArrowDown size={16} /></a></div>
      </main>
      <div className="hero-foot wrap mono"><span>A closer look at a changing planet</span><a href="#platform">Scroll to explore <ArrowDown size={16} /></a></div>
      <div className="corner corner-tl" /><div className="corner corner-br" />
    </section>

    <section className="platform-section section-pad wrap" id="platform" aria-labelledby="platform-heading">
      <div className="section-intro"><div><span className="mono section-label">01 / THE BIGGER PICTURE</span><h2 id="platform-heading">Know your land.<br />See what’s changing.</h2></div><p>The next season starts with a clearer view. Polle brings your fields, Earth observations, and crop rotation planning into one connected workspace.</p></div>
      <div className="platform-layout">
        <div ref={atlas} className={`atlas-preview ${showBoundaries ? 'show-boundaries' : ''}`}>
          {/* Layer control precedes the outlines in the DOM so keyboard focus reaches the mode switch first. */}
          <div className={`layer-control ${boundaries ? 'on-boundaries' : ''}`} role="group" aria-label="Illustrative map layers"><button aria-pressed={!boundaries} onClick={() => setBoundaries(false)}>Landscape</button><button aria-pressed={boundaries} onClick={() => setBoundaries(true)}>Field boundaries</button></div>
          {/* The scan mounts with the boundaries layer, so every switch back replays one satellite pass over the outlines. */}
          <FieldLines scan={showBoundaries} onSelect={showBoundaries ? setSelectedField : undefined} selected={selectedField} />
          <div className="atlas-caption">
            {showBoundaries && <p className="atlas-readout" aria-live="polite">{selectedField ? <><strong>{fieldLabel(selectedField)}</strong>Example outline, not a saved field</> : 'Select a field to inspect its outline'}</p>}
            <span className="mono">Illustrative preview · Not live field data</span>
          </div>
        </div>
        <ol className="workflow">{workflow.map((step, index) => <li key={step.title}><span className="workflow-number mono">0{index + 1}</span><div><h3>{step.title}</h3><p>{step.text}</p></div></li>)}</ol>
      </div>
      <div className="platform-note"><span className="status-dot" />Available today: farms, fields, climate and drought context, and crop rotation planning.<span>Sign up free and map your first field.</span></div>
    </section>

    <section className="earth-section" id="earth-intelligence" aria-labelledby="earth-heading"><div className="wrap section-pad">
      <div className="earth-top"><span className="mono section-label">02 / FROM ORBIT TO OPPORTUNITY</span><Globe2 size={23} strokeWidth={1} aria-hidden="true" /></div>
      <div className="earth-layout"><div className="earth-copy"><h2 id="earth-heading">A planet of data.<br /><em>A field of possibilities.</em></h2><p>Changing rainfall. Shifting seasons. Soil under pressure. Understanding the bigger picture starts with observations beyond the farm gate.</p><p>Polle connects open NASA and NOAA datasets with your land, turning environmental context into crop rotation plans you can compare.</p><a className="text-link light" href="https://www.earthdata.nasa.gov/" target="_blank" rel="noreferrer">Explore NASA Earthdata <ArrowUpRight size={17} /></a></div>
        <div className="data-sources"><div className="data-source"><span className="mono">01 / ATMOSPHERE</span><h3>Climate through time</h3><p>Ten years of NASA POWER daily temperature and rainfall at each field set the weather your crop rotation strategies are scored against.</p><a href="https://power.larc.nasa.gov/" target="_blank" rel="noreferrer">About NASA POWER <ArrowUpRight size={14} /></a></div>
          <div className="data-source"><span className="mono">02 / SOIL & LAND</span><h3>A closer look below</h3><p>NASA SMAP root-zone soil moisture is a layer on the climate map, beside MODIS vegetation and surface temperature. It is shown for context and not yet used in field scoring.</p><a href="https://smap.jpl.nasa.gov/" target="_blank" rel="noreferrer">About NASA SMAP <ArrowUpRight size={14} /></a></div>
          <p className="source-note mono">Live: NASA POWER field climate · NOAA CPC drought · NASA GIBS map layers</p></div>
      </div>
      <div className="earth-footer"><a href="https://www.spaceappschallenge.org/2026/find-a-team/clique/?tab=project" target="_blank" rel="noreferrer">Built for NASA Space Apps Challenge 2026 <ArrowUpRight size={14} /></a><span>Independent project. Not affiliated with or endorsed by NASA.</span></div>
    </div></section>

    <section className="closing-section wrap"><span className="mono section-label">ROOTED IN POSSIBILITY</span><div><h2>The future of your land<br />starts with a clearer view.</h2><Link className="button button-dark" to="/register">Create your workspace <ArrowUpRight size={20} /></Link></div><p>Your account is the first seed. Let’s see what grows.</p></section>
    <footer className="site-footer wrap"><div className="footer-top"><Brand /><a href="#main" className="text-link">Back to top <ArrowUpRight size={16} /></a></div><div className="footer-bottom"><span>© {copyrightYear} Polle</span><span>Earth intelligence. Human possibility.</span><div><Link to="/sources">Data &amp; sources</Link><Link to="/login">Log in</Link><Link to="/register">Get started</Link></div></div></footer>
  </>
}
