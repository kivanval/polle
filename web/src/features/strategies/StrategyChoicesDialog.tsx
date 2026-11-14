import { useEffect, useRef, useState, type KeyboardEvent } from 'react'
import { Plus, Trash2, X } from 'lucide-react'
import { REPLENISHMENT_LEVELS, type CustomStrategy, type ReplenishmentLevel, type StrategyPreset } from './api'
import { DESCRIPTION_MAX_LENGTH, LEVEL_LABEL, MAX_STRATEGIES, NAME_MAX_LENGTH, NEW_CUSTOM, addHint, chosenPresets, choiceCount, customDescription,
  customName, customTitle, defaultCustomDescription, defaultCustomName, presetMatches, sameCustom, tidyCustom, type ChoiceState } from './choices'
import { settingsText } from './details'

const FLAGS: { key: 'allowAgronomicRisks' | 'allowSoilDepletion' | 'allowMissingData'; label: string; hint: string }[] = [
  { key: 'allowAgronomicRisks', label: 'Accept agronomic risks', hint: 'Frost, heat, drought or waterlogging the solver flags for a crop.' },
  { key: 'allowSoilDepletion', label: 'May draw the soil down', hint: 'Use up the soil’s nitrogen, phosphorus and potassium for higher yields now.' },
  { key: 'allowMissingData', label: 'Estimate missing figures', hint: 'Plan crops whose figures are incomplete, with estimates in their place.' },
]

const customSettings = (custom: CustomStrategy) => settingsText({ ...custom, irrigation: custom.irrigation !== 'OFF', fertilizer: custom.fertilizer !== 'OFF',
  irrigationLevel: custom.irrigation, fertilizerLevel: custom.fertilizer })

/**
 * The gear next to "Suggest strategies": which presets run and the farmer's own solver settings. Changes apply at once;
 * the view keeps them for the next run.
 */
export function StrategyChoicesDialog({ presets, loading, choices, onChange, onClose }: {
  presets: StrategyPreset[]; loading: boolean; choices: ChoiceState; onChange: (choices: ChoiceState) => void; onClose: () => void
}) {
  const [draft, setDraft] = useState<CustomStrategy>(NEW_CUSTOM)
  const panel = useRef<HTMLElement>(null)
  const keys = presets.map(preset => preset.key)
  const chosen = chosenPresets(choices, keys)
  const count = choiceCount(choices, keys)
  const full = count >= MAX_STRATEGIES
  const duplicate = choices.custom.some(custom => sameCustom(custom, draft))
  const twin = presets.find(preset => presetMatches(preset, draft))

  /** Adds the draft with its name and description tidied (blank ones are left out), then clears them for the next one. */
  function add() {
    onChange({ ...choices, custom: [...choices.custom, tidyCustom(draft)] })
    setDraft({ ...draft, name: '', description: '' })
  }

  useEffect(() => {
    const opener = document.activeElement as HTMLElement | null
    return () => { requestAnimationFrame(() => opener?.isConnected && opener.focus()) }
  }, [])

  function togglePreset(key: string) {
    const next = chosen.includes(key) ? chosen.filter(item => item !== key) : keys.filter(item => item === key || chosen.includes(item))
    onChange({ ...choices, presets: next.length === keys.length ? null : next })
  }

  /** Keeps Tab inside the dialog; Escape closes only the dialog, not the view behind it. */
  function onKeyDown(event: KeyboardEvent) {
    if (event.key === 'Escape') { event.stopPropagation(); event.nativeEvent.stopImmediatePropagation(); onClose(); return }
    if (event.key !== 'Tab' || !panel.current) return
    event.stopPropagation()
    const focusable = [...panel.current.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), select:not(:disabled)')]
      .filter(element => element.offsetParent !== null)
    const first = focusable[0], last = focusable.at(-1)
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus() }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus() }
  }

  const level = (value: string) => value as ReplenishmentLevel

  return <div className="strategy-choices-backdrop" onMouseDown={event => { if (event.target === event.currentTarget) onClose() }}>
    <section ref={panel} className="strategy-choices" role="dialog" aria-modal="true" aria-labelledby="strategy-choices-title" onKeyDown={onKeyDown}>
      <header className="strategy-view-head">
        <h3 id="strategy-choices-title">Strategies to compare</h3>
        <button type="button" className="button button-quiet button-icon" aria-label="Close strategy settings" autoFocus onClick={onClose}><X size={16} /></button>
      </header>
      <p className="strategy-note">{`Each strategy is planned separately, up to ${MAX_STRATEGIES} at a time. Your choices are kept for your next suggestions.`}</p>

      <h4 className="strategy-choices-heading">Presets</h4>
      {loading ? <p className="strategy-note">Loading presets…</p> : <ul className="strategy-choices-list">
        {presets.map(preset => <li key={preset.key}>
          <label className="strategy-choice">
            <input type="checkbox" checked={chosen.includes(preset.key)} disabled={!chosen.includes(preset.key) && full} onChange={() => togglePreset(preset.key)} />
            <span className="strategy-choice-text"><strong>{preset.name}</strong> <span className="strategy-note">{preset.summary}</span>
              <ul className="strategy-settings">{settingsText(preset.settings).map(text => <li key={text}><span>{text}</span></li>)}</ul></span>
          </label>
        </li>)}
      </ul>}

      <h4 className="strategy-choices-heading">Your own strategies</h4>
      {choices.custom.length === 0 ? <p className="strategy-note">None yet. Set the options below and add one.</p>
        : <ul className="strategy-choices-list">{choices.custom.map((custom, index) => <li key={customTitle(custom) + customSettings(custom).join()} className="strategy-choice">
            <span className="strategy-choice-text"><strong>{customName(custom)}</strong> <span className="strategy-note">{customDescription(custom)}</span>
              <ul className="strategy-settings">{customSettings(custom).map(text => <li key={text}><span>{text}</span></li>)}</ul></span>
            <button type="button" className="button button-quiet button-icon" aria-label={`Remove ${customName(custom)}`}
              onClick={() => onChange({ ...choices, custom: choices.custom.filter((_, item) => item !== index) })}><Trash2 size={14} /></button>
          </li>)}</ul>}

      <fieldset className="strategy-custom-form">
        <legend>New strategy</legend>
        <label className="strategy-custom-wide">Name<input type="text" value={draft.name ?? ''} maxLength={NAME_MAX_LENGTH}
          placeholder={defaultCustomName(draft)} onChange={event => setDraft({ ...draft, name: event.target.value })} /></label>
        <label className="strategy-custom-wide">Description <span>(optional)</span><textarea rows={2} value={draft.description ?? ''} maxLength={DESCRIPTION_MAX_LENGTH}
          placeholder={defaultCustomDescription(draft)} onChange={event => setDraft({ ...draft, description: event.target.value })} /></label>
        <label>Water<select value={draft.irrigation} onChange={event => setDraft({ ...draft, irrigation: level(event.target.value) })}>
          {REPLENISHMENT_LEVELS.map(value => <option key={value} value={value}>{LEVEL_LABEL[value]}</option>)}</select></label>
        <label>Fertilizer<select value={draft.fertilizer} onChange={event => setDraft({ ...draft, fertilizer: level(event.target.value) })}>
          {REPLENISHMENT_LEVELS.map(value => <option key={value} value={value}>{LEVEL_LABEL[value]}</option>)}</select></label>
        {FLAGS.map(flag => <label key={flag.key} className="strategy-choice" title={flag.hint}>
          <input type="checkbox" checked={draft[flag.key]} onChange={event => setDraft({ ...draft, [flag.key]: event.target.checked })} />
          <span>{flag.label}</span></label>)}
        {/* Its own row under the options, so adding is the obvious next step once they are set. */}
        <div className="strategy-custom-foot">
          <p className="strategy-note" role="status">{addHint({ full, duplicate, twin: twin?.name })}</p>
          <button type="button" className="button button-dark strategy-add-button" disabled={full || duplicate} onClick={add}>
            <Plus size={16} aria-hidden="true" /><span>Add strategy</span></button>
        </div>
      </fieldset>

      <footer className="strategy-choices-foot">
        <span className={count === 0 ? 'field-error' : 'strategy-note'} role="status">
          {count === 0 ? 'Choose at least one strategy.' : `${count} ${count === 1 ? 'strategy' : 'strategies'} will be planned.`}</span>
        <div className="strategy-view-actions">
          <button type="button" className="button button-quiet" onClick={() => onChange({ presets: null, custom: [] })}><span>Reset to presets</span></button>
          <button type="button" className="button button-dark" disabled={count === 0} onClick={onClose}><span>Done</span></button>
        </div>
      </footer>
    </section>
  </div>
}
