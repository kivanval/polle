import { useState, type FormEvent } from 'react'
import { Check, Trash2 } from 'lucide-react'
import { Drawer } from '@/features/finance/Drawer'
import { TractorSpinner } from '@/shared/ui/Tractor'
import type { Field } from './api'
import { problemMessage } from './api'
import { CropPicker } from './CropPicker'
import { formatDate, overlaps, periodText, type Slot } from './periods'
import { cropName, profileOf, useDeletePlanting, useFertilizers, useSavePlanting, type Crop, type Planting } from './plantings'
import { SeasonDates } from './SeasonDates'
import '@/styles/catalog.css'

export type { Slot } from './periods'

type FormValues = {
  cropId: number | null; profileId: number | null; plantingDate: string; harvestingDate: string
  /** Text as typed; empty is none. */
  irrigationMm: string; fertilizerId: string; fertilizerRate: string
}

const amount = new Intl.NumberFormat('en', { maximumFractionDigits: 0 })
/** A typed number, or null when the box is empty or not a number. */
const typed = (text: string) => text.trim() === '' || !Number.isFinite(Number(text)) ? null : Number(text)

/**
 * The season a crop is booked under, which strategies also use: the year it is harvested. A crop still growing keeps
 * the season it was saved with while its sowing date stays, otherwise the year it is sown.
 */
function seasonOf(sown: string, harvested: string, planting?: Planting) {
  if (harvested) return Number(harvested.slice(0, 4))
  if (planting && planting.plantingDate === sown) return planting.seasonYear
  return Number(sown.slice(0, 4))
}

/**
 * Adds a crop to a field, or edits or removes one. Crops on a field never overlap, so sowing is required and harvest
 * must end before the next crop starts (`slot.maxHarvest`). A crop with several growing profiles asks which one. The
 * water and fertilizer planned for the season go with the crop, and "Evaluate my plan" scores it with them.
 */
export function PlantingModal({ field, crops, periods, planting, slot, focusHarvest, onClose }: {
  field: Field; crops: Crop[]; periods: Planting[]; planting?: Planting; slot?: Slot; focusHarvest?: boolean; onClose: () => void
}) {
  const savePlanting = useSavePlanting(field.farmId)
  const deletePlanting = useDeletePlanting(field.farmId)
  const fertilizers = useFertilizers()
  const existing = planting ? profileOf(crops, planting.cropProfileId) : null
  const [values, setValues] = useState<FormValues>({ cropId: existing?.crop.id ?? null,
    profileId: existing?.profile.id ?? null, plantingDate: planting?.plantingDate ?? slot?.plantingDate ?? '', harvestingDate: planting?.harvestingDate ?? '',
    irrigationMm: planting?.irrigationMm ? String(planting.irrigationMm) : '',
    fertilizerId: planting?.fertilizerMaterialId ? String(planting.fertilizerMaterialId) : '',
    fertilizerRate: planting?.fertilizerRateKgHa ? String(planting.fertilizerRateKgHa) : '' })
  const [error, setError] = useState('')
  const busy = savePlanting.isPending || deletePlanting.isPending
  const crop = crops.find(candidate => candidate.id === values.cropId) ?? null
  const id = planting?.id ?? 'new'

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (busy) return
    const end = values.harvestingDate || null
    if (!crop) { setError('Choose a crop.'); return }
    if (!values.profileId) { setError(`Choose how ${crop.name} is grown.`); return }
    if (!values.plantingDate) { setError('Add the sowing date. Crops on a field can’t overlap, so the start is required.'); return }
    if (end && end < values.plantingDate) { setError('Harvest can’t come before sowing.'); return }
    if (slot?.maxHarvest && (!end || end > slot.maxHarvest)) { setError(`The next crop is sown after ${formatDate(slot.maxHarvest)}. Add a harvest date on or before it.`); return }
    const irrigation = typed(values.irrigationMm), rate = typed(values.fertilizerRate)
    if (values.irrigationMm.trim() && (irrigation === null || irrigation < 0 || irrigation > 5000)) { setError('Irrigation over the season must be between 0 and 5000 mm.'); return }
    if (values.fertilizerId && (rate === null || rate <= 0 || rate > 10000)) { setError('Add how many kilograms of the fertilizer go on each hectare (up to 10000).'); return }
    const clash = periods.find(other => other.id !== planting?.id && overlaps(values.plantingDate, end, other.plantingDate, other.harvestingDate))
    if (clash) { setError(`These dates overlap ${cropName(crops, clash.cropId)} (${periodText(clash)}). One crop grows on a field at a time.`); return }
    try {
      await savePlanting.mutateAsync({ fieldId: field.id, plantingId: planting?.id, input: {
        cropProfileId: values.profileId, seasonYear: seasonOf(values.plantingDate, values.harvestingDate, planting), plantingDate: values.plantingDate,
        harvestingDate: end, notes: planting?.notes ?? null, irrigationMm: irrigation && irrigation > 0 ? irrigation : null,
        fertilizerMaterialId: values.fertilizerId ? Number(values.fertilizerId) : null, fertilizerRateKgHa: values.fertilizerId ? rate : null } })
      onClose()
    } catch (reason) { setError(problemMessage(reason)) }
  }

  async function remove() {
    if (!planting || busy) return
    try { await deletePlanting.mutateAsync(planting.id); onClose() } catch (reason) { setError(problemMessage(reason)) }
  }

  const area = field.areaHectares > 0 ? field.areaHectares : null
  const water = typed(values.irrigationMm), perHectare = typed(values.fertilizerRate)
  const change = (patch: Partial<FormValues>) => { setValues(current => ({ ...current, ...patch })); setError('') }
  const title = planting ? cropName(crops, planting.cropId) : `Add a crop to ${field.name}`

  return <Drawer kicker={planting ? `Crop on ${field.name}` : 'New crop'} title={title} busy={busy} onClose={onClose}
    footer={<footer className="catalog-drawer-foot">
      <button type="submit" form={`planting-form-${id}`} className="button button-dark" disabled={busy}>
        {savePlanting.isPending ? <TractorSpinner size={15} /> : <Check size={15} />}{planting ? 'Save crop' : 'Add crop'}</button>
      <button type="button" className="button button-quiet" onClick={onClose} disabled={busy}>Cancel</button>
      {planting && <button type="button" className="button button-quiet catalog-delete" onClick={() => void remove()} disabled={busy}>
        {deletePlanting.isPending ? <TractorSpinner size={15} /> : <Trash2 size={15} />}Remove from plan</button>}
    </footer>}>
    <form id={`planting-form-${id}`} className="catalog-form" onSubmit={submit} noValidate>
      <div className="catalog-field is-wide">
        <label htmlFor={`crop-${id}`}>Crop</label>
        <CropPicker id={`crop-${id}`} crops={crops} value={crop} disabled={busy || crops.length === 0} autoFocus={!focusHarvest && !planting}
          onChange={picked => { setValues(current => ({ ...current, cropId: picked.id, profileId: picked.profiles.length === 1 ? picked.profiles[0].id : null })); setError('') }} />
      </div>
      {crop && crop.profiles.length > 1 && <div className="catalog-field is-wide">
        <label htmlFor={`profile-${id}`}>How it’s grown</label>
        <select id={`profile-${id}`} value={values.profileId ?? ''} disabled={busy}
          onChange={event => { setValues(current => ({ ...current, profileId: Number(event.target.value) })); setError('') }}>
          <option value="" disabled>Choose a growing system</option>
          {crop.profiles.map(profile => <option key={profile.id} value={profile.id}>{profile.systemName}</option>)}
        </select>
      </div>}
      <div className="catalog-field is-wide">
        <SeasonDates id={id} sown={values.plantingDate} harvested={values.harvestingDate} maxHarvest={slot?.maxHarvest ?? null}
          occupied={periods.filter(other => other.id !== planting?.id).map(other => ({ from: other.plantingDate, to: other.harvestingDate, label: cropName(crops, other.cropId) }))}
          disabled={busy} focusHarvest={focusHarvest}
          onChange={dates => { setValues(current => ({ ...current,
            ...(dates.sown !== undefined && { plantingDate: dates.sown }), ...(dates.harvested !== undefined && { harvestingDate: dates.harvested }) })); setError('') }} />
        <p className="crop-form-hint">Leave the harvest date empty while the crop is still growing.</p>
      </div>
      <div className="catalog-field">
        <label htmlFor={`irrigation-${id}`}>Irrigation over the season, mm<span className="optional"> optional</span></label>
        <input id={`irrigation-${id}`} className="is-numeric" type="number" inputMode="decimal" min={0} max={5000} step="any" placeholder="None, rain-fed"
          value={values.irrigationMm} disabled={busy} aria-describedby={`irrigation-hint-${id}`} onChange={event => change({ irrigationMm: event.target.value })} />
        <p id={`irrigation-hint-${id}`} className="crop-form-hint">{water && water > 0 && area
          ? `About ${amount.format(water * 10 * area)} m³ of water over ${amount.format(area)} ha.` : '1 mm is 10 m³ of water per hectare.'}</p>
      </div>
      <div className="catalog-field">
        <label htmlFor={`fertilizer-${id}`}>Fertilizer<span className="optional"> optional</span></label>
        <select id={`fertilizer-${id}`} value={values.fertilizerId} disabled={busy || fertilizers.isPending}
          onChange={event => change({ fertilizerId: event.target.value, ...(event.target.value ? {} : { fertilizerRate: '' }) })}>
          <option value="">{fertilizers.isPending ? 'Loading…' : 'None'}</option>
          {/* A saved product the list no longer has stays choosable, so saving doesn't drop it unseen. */}
          {values.fertilizerId && fertilizers.data && !fertilizers.data.some(item => String(item.id) === values.fertilizerId)
            && <option value={values.fertilizerId}>{`Product ${values.fertilizerId}`}</option>}
          {(fertilizers.data ?? []).map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
        </select>
        {fertilizers.error && <p className="field-error" role="alert">{problemMessage(fertilizers.error)}{' '}
          <button type="button" className="text-link" disabled={fertilizers.isFetching} onClick={() => void fertilizers.refetch()}>Try again</button></p>}
      </div>
      {values.fertilizerId && <div className="catalog-field is-wide">
        <label htmlFor={`fertilizer-rate-${id}`}>Fertilizer per hectare over the season, kg</label>
        <input id={`fertilizer-rate-${id}`} className="is-numeric" type="number" inputMode="decimal" min={0} max={10000} step="any" placeholder="e.g. 250"
          value={values.fertilizerRate} disabled={busy} aria-describedby={`fertilizer-hint-${id}`} onChange={event => change({ fertilizerRate: event.target.value })} />
        <p id={`fertilizer-hint-${id}`} className="crop-form-hint">{perHectare && perHectare > 0 && area
          ? `About ${amount.format(perHectare * area)} kg of the product for the whole field (${amount.format(area)} ha).` : 'Kilograms of the product itself, as bought, not of N, P or K.'}</p>
      </div>}
    </form>
    {error && <p className="field-error catalog-form-error" role="alert">{error}</p>}
  </Drawer>
}
