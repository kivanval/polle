import { useState } from 'react'
import { Link } from 'react-router'
import { useQueries } from '@tanstack/react-query'
import { ArrowUpRight, CornerDownRight, Plus, Search } from 'lucide-react'
import { farmKeys, mappedHectares, nestFields, problemMessage, topLevelFields, useFarms, type Farm, type Field } from '@/features/farms/api'
import { formatHectares } from '@/features/farms/geo'
import { fieldCropLabel, plantingApi, useCrops, type Crop, type Planting } from '@/features/farms/plantings'
import { FieldGlyph } from '@/features/map/FieldGlyph'
import { apiFetch } from '@/shared/api/client'
import { TractorLoader } from '@/shared/ui/Tractor'

/** Every field of every farm, with subfields under their field. A row opens the field on its farm's map. */
export function FieldsPage() {
  const farmQuery = useFarms()
  const farms = farmQuery.data ?? []
  const fieldLists = useQueries({ queries: farms.map(farm => ({ queryKey: farmKeys.fields(farm.id), queryFn: () => apiFetch<Field[]>(`/farms/${farm.id}/fields`) })) })
  const plantingLists = useQueries({ queries: farms.map(farm => ({ queryKey: farmKeys.plantings(farm.id), queryFn: () => plantingApi.listFarm(farm.id) })) })
  const crops = useCrops()
  const [farmId, setFarmId] = useState<string | null>(null)
  const [query, setQuery] = useState('')

  const error = farmQuery.error ?? fieldLists.find(item => item.error)?.error
  if (error) return <div className="atlas-state"><h1>Your fields didn’t load.</h1><p role="alert">{problemMessage(error)}</p>
    <button className="button button-dark" onClick={() => { void farmQuery.refetch(); fieldLists.forEach(item => { void item.refetch() }) }}>Try again</button></div>
  if (farmQuery.isPending || fieldLists.some(item => item.isPending)) return <div className="atlas-state" role="status"><TractorLoader /><p>Loading your fields…</p></div>

  const fields = fieldLists.flatMap(item => item.data ?? [])
  const plantings = plantingLists.flatMap(item => item.data ?? [])
  // Crop history enriches the rows; without it the list still shows every field.
  const cropsKnown = !plantingLists.some(item => item.isPending || item.error) && !crops.isPending && !crops.error
  const top = topLevelFields(fields)

  if (farms.length === 0) return <>
    <Heading fields={0} subfields={0} area={0} />
    <section className="farms-empty"><img src="/images/contours.webp" alt="" />
      <div><h2>No farms yet.</h2><p>Fields belong to a farm. Create a farm, then outline its fields on the map.</p>
        <Link className="button button-dark" to="/dashboard/farms/new"><Plus size={17} />Create farm</Link></div></section>
  </>

  const needle = query.trim().toLocaleLowerCase()
  const shown = farms.filter(farm => !farmId || farm.id === farmId)
  return <div className="fields-page">
    <Heading fields={top.length} subfields={fields.length - top.length} area={mappedHectares(fields)} />
    <div className="fields-tools">
      <div className="finance-filter" role="group" aria-label="Show farms">
        <button type="button" aria-pressed={!farmId} onClick={() => setFarmId(null)}>All farms <span className="mono">{farms.length}</span></button>
        {farms.map(farm => <button key={farm.id} type="button" aria-pressed={farmId === farm.id} onClick={() => setFarmId(farm.id)}>{farm.name}</button>)}
      </div>
      <label className="fields-search"><Search size={15} aria-hidden="true" /><span className="visually-hidden">Find a field</span>
        <input type="search" value={query} placeholder="Find a field" onChange={event => setQuery(event.target.value)} /></label>
    </div>
    {shown.map(farm => <FarmFields key={farm.id} farm={farm} fields={fields.filter(field => field.farmId === farm.id)} needle={needle}
      plantings={plantings} crops={crops.data ?? []} cropsKnown={cropsKnown} />)}
  </div>
}

function Heading({ fields, subfields, area }: { fields: number; subfields: number; area: number }) {
  return <div className="workspace-heading"><span className="mono section-label">FIELDS</span><h1>Every field, in one list.</h1>
    <p>{fields ? `${fields} ${fields === 1 ? 'field' : 'fields'}${subfields ? ` and ${subfields} ${subfields === 1 ? 'subfield' : 'subfields'}` : ''} covering ${formatHectares(area)} of mapped land. Open one to see it on its farm’s map.`
      : 'Outline fields on a farm’s map and they all appear here.'}</p></div>
}

function FarmFields({ farm, fields, needle, plantings, crops, cropsKnown }: {
  farm: Farm; fields: Field[]; needle: string; plantings: Planting[]; crops: Crop[]; cropsKnown: boolean
}) {
  // A match on a subfield keeps its parent visible, and a match on a parent keeps its subfields, so nesting stays readable.
  const matches = (field: Field) => field.name.toLocaleLowerCase().includes(needle)
  const visible = nestFields(fields).filter(field => !needle || matches(field)
    || fields.some(other => (other.parentFieldId === field.id || other.id === field.parentFieldId) && matches(other)))
  if (needle && visible.length === 0) return null
  return <section className="finance-card fields-farm" aria-labelledby={`farm-${farm.id}`}>
    <header className="fields-farm-head">
      <div><h2 id={`farm-${farm.id}`}>{farm.name}</h2>
        <p>{topLevelFields(fields).length} {topLevelFields(fields).length === 1 ? 'field' : 'fields'} · {formatHectares(mappedHectares(fields))}{farm.location ? ` · ${farm.location}` : ''}</p></div>
      <Link className="subtle-link" to={`/dashboard/farms/${farm.id}`}>Open farm map <ArrowUpRight size={13} /></Link>
    </header>
    {fields.length === 0
      ? <p className="dashboard-empty">No fields on this farm yet. <Link className="text-button" to={`/dashboard/farms/${farm.id}`}>Draw the first one</Link></p>
      : <ul className="fields-rows">{visible.map(field => {
          const crop = fieldCropLabel(field, fields, plantings, crops)
          return <li key={field.id} className={field.parentFieldId ? 'is-subfield' : ''}>
            <Link to={`/dashboard/farms/${farm.id}?field=${field.id}`} className="fields-row">
              {field.parentFieldId && <CornerDownRight size={13} className="field-row-branch" aria-hidden="true" />}
              <FieldGlyph boundary={field.boundary} />
              <span className="fields-row-name">{field.name}</span>
              <span className={`fields-row-crop ${crop.growing ? '' : 'is-empty'}`}>{cropsKnown ? crop.text : ''}</span>
              <span className="fields-row-area mono">{formatHectares(field.areaHectares)}</span>
            </Link>
          </li>
        })}</ul>}
  </section>
}
