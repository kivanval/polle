import { Link } from 'react-router'
import { ArrowUpRight } from 'lucide-react'
import { Brand } from '@/shared/ui/Brand'

type Source = { name: string; url: string; use: string }
type Group = { id: string; title: string; summary: string; sources: Source[] }

/**
 * The data and services Polle is built on, each with what it is used for. Only sources the app actually reads or shows
 * are listed; each link is the provider's own page.
 */
export const SOURCE_GROUPS: Group[] = [
  {
    id: 'earth', title: 'NASA Earth observation', summary: 'Weather at each field and the satellite layers of the climate map.',
    sources: [
      { name: 'NASA POWER', url: 'https://power.larc.nasa.gov/', use: 'Daily temperature and rain at each field over ten years, the weather suggested plans are scored against.' },
      { name: 'NASA GIBS', url: 'https://www.earthdata.nasa.gov/engage/open-data-services-software/earthdata-developer-portal/gibs-api', use: 'Satellite map tiles and their colour maps, read to give a value at a picked place.' },
      { name: 'NASA SMAP', url: 'https://smap.jpl.nasa.gov/', use: 'Root-zone soil moisture layer on the climate map.' },
      { name: 'NASA MODIS', url: 'https://modis.gsfc.nasa.gov/', use: 'Vegetation (NDVI) and land surface temperature layers on the climate map.' },
    ],
  },
  {
    id: 'climate', title: 'Climate and drought', summary: 'The Polle trend model and Europe’s official drought indicator.',
    sources: [
      { name: 'NOAA CPC Global Unified Gauge-Based Analysis of Daily Precipitation', url: 'https://psl.noaa.gov/data/gridded/data.cpc.globalprecip.html', use: 'Rain behind the Polle water-balance, rain and drought trends.' },
      { name: 'NOAA CPC Global Daily Temperature', url: 'https://psl.noaa.gov/data/gridded/data.cpc.globaltemp.html', use: 'Temperature behind the Polle air-temperature trend and evaporation demand.' },
      { name: 'Copernicus European Drought Observatory', url: 'https://drought.emergency.copernicus.eu/', use: 'The Combined Drought Indicator layer for Europe.' },
    ],
  },
  {
    id: 'crops', title: 'Crops and agronomy', summary: 'Where the crop catalog’s growing ranges and seasons come from.',
    sources: [
      { name: 'FAO EcoCrop', url: 'https://ecocrop.apps.fao.org/polle/srv/en/home', use: 'Temperature, rain and soil ranges crops grow in.' },
      { name: 'FAO Crop Calendar', url: 'https://cropcalendar.apps.fao.org/#/home', use: 'Usual sowing and harvest seasons.' },
      { name: 'FAO GAEZ', url: 'https://gaez.fao.org/', use: 'Agro-ecological zones and attainable yields.' },
    ],
  },
  {
    id: 'maps', title: 'Maps and places', summary: 'The base map, satellite photo and place search.',
    sources: [
      { name: 'OpenStreetMap', url: 'https://www.openstreetmap.org/copyright', use: 'Map data under the base map and place names.' },
      { name: 'Nominatim', url: 'https://nominatim.org/', use: 'Searching for a place and naming a picked point.' },
      { name: 'CARTO', url: 'https://carto.com/attributions', use: 'The base map style.' },
      { name: 'Esri World Imagery', url: 'https://www.arcgis.com/home/item.html?id=10df2279f9684e4a9f6a7f08febac2a9', use: 'The satellite photo and its place labels.' },
    ],
  },
  {
    id: 'photos', title: 'Photography', summary: 'Real photographs from Wikimedia Commons behind the site’s landscapes, cropped, resized and colour-graded for the layout.',
    sources: [
      { name: 'Patchwork Road in Biei, Hokkaido, by 663highland (CC BY 2.5)', url: 'https://commons.wikimedia.org/wiki/File:140724_Biei_Hokkaido_Japan01s8.jpg', use: 'The landing page hero and the platform preview. Its field outlines are examples, not live data.' },
      { name: 'Fields in Gladbeck, Nottuln, by Dietmar Rabich (CC BY-SA 4.0)', url: 'https://commons.wikimedia.org/wiki/File:Nottuln,_Gladbeck,_Felder_--_2024_--_0119-23.jpg', use: 'The sunrise beside the log-in and sign-up forms.' },
      { name: 'Moravská Zebra, strip fields near Bošovice, by Konstantin zhdanov (CC BY-SA 4.0)', url: 'https://commons.wikimedia.org/wiki/File:Moravsk%C3%A1_Zebra.jpg', use: 'The contour-farming photo on empty workspace pages.' },
    ],
  },
]

/** A public page listing every data source and service, linked from the landing page. */
export function SourcesPage() {
  return <div className="public-climate sources-page">
    <header className="public-climate-bar">
      <Brand light />
      <span className="public-climate-title mono">DATA & SOURCES</span>
      <nav aria-label="Pages"><Link className="public-climate-login" to="/climate">Climate map</Link>
        <Link className="button button-lime" to="/register">Get started <ArrowUpRight size={15} /></Link></nav>
    </header>
    <main className="sources-main">
      <h1>Data and sources</h1>
      <p className="sources-lede">Polle is built on open Earth observation, climate and agronomy data. These are the sources it reads, and what each is used for.</p>
      {SOURCE_GROUPS.map(group => <section key={group.id} className="sources-group" aria-labelledby={`sources-${group.id}`}>
        <h2 id={`sources-${group.id}`}>{group.title}</h2>
        <p className="sources-summary">{group.summary}</p>
        <ul>{group.sources.map(source => <li key={source.url}>
          <a href={source.url} target="_blank" rel="noreferrer">{source.name} <ArrowUpRight size={13} aria-hidden="true" /></a>
          <span>{source.use}</span>
        </li>)}</ul>
      </section>)}
      <p className="sources-note"><a href="https://www.spaceappschallenge.org/2026/find-a-team/clique/?tab=project" target="_blank" rel="noreferrer">Built for NASA Space Apps Challenge 2026</a>.
        An independent project, not affiliated with or endorsed by NASA.</p>
    </main>
  </div>
}
