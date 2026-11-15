import { REPLENISHMENT_LEVELS, type CustomStrategy, type ReplenishmentLevel, type StrategyChoice, type StrategyPreset } from './api'

/** The solver's dosing steps per level, as felder defines them. */
export const LEVEL_LABEL: Record<ReplenishmentLevel, string> = {
  OFF: 'Off',
  BINARY: 'On or off (0 or 100%)',
  TERNARY: '3 steps (0, 50, 100%)',
  QUATERNARY: '4 steps (0, 50, 80, 100%)',
  CONTINUOUS: 'Finely (every 20%)',
}

/** A level as a few words after "water" or "fertilizer"; a level this app doesn't know yet reads as given. */
export function levelWords(level: string): string {
  switch (level) {
    case 'BINARY': return 'on or off'
    case 'TERNARY': return 'in 3 steps'
    case 'QUATERNARY': return 'in 4 steps'
    case 'CONTINUOUS': return 'dosed finely'
    default: return level.toLowerCase()
  }
}

/** Which presets run (null: all of them, also ones added later) and the farmer's own strategies. */
export type ChoiceState = { presets: string[] | null; custom: CustomStrategy[] }
export const DEFAULT_CHOICES: ChoiceState = { presets: null, custom: [] }
/** The API's limit on strategies per run. */
export const MAX_STRATEGIES = 12
/** The API's limits on a custom strategy's name and description. */
export const NAME_MAX_LENGTH = 80
export const DESCRIPTION_MAX_LENGTH = 400

export const NEW_CUSTOM: CustomStrategy = { allowMissingData: false, allowAgronomicRisks: true, allowSoilDepletion: false, irrigation: 'TERNARY', fertilizer: 'QUATERNARY' }

/**
 * The choices are kept per account: a farmer's own strategies are theirs, so another account signed in on the same
 * browser never sees them. The first version kept one set for the whole browser; whose it was can't be told, so it is
 * never read, only removed.
 */
const LEGACY_KEY = 'ecocrop.strategy-choices.v1'
export const choicesKey = (userId: string) => `ecocrop.strategy-choices.v2.${userId}`

const isLevel = (value: unknown): value is ReplenishmentLevel => REPLENISHMENT_LEVELS.includes(value as ReplenishmentLevel)
const optionalText = (value: unknown) => value === undefined || typeof value === 'string'
const isCustom = (value: unknown): value is CustomStrategy => {
  const item = value as Partial<CustomStrategy> | null
  return !!item && typeof item.allowMissingData === 'boolean' && typeof item.allowAgronomicRisks === 'boolean'
    && typeof item.allowSoilDepletion === 'boolean' && isLevel(item.irrigation) && isLevel(item.fertilizer)
    && optionalText(item.name) && optionalText(item.description)
}

const browserStorage = (): Storage | undefined => { try { return globalThis.localStorage } catch { return undefined } }

/**
 * The choices this account last used in this browser; the defaults when there are none, they can't be read, or no
 * account is known yet (the signed-in user still loading).
 */
export function readChoices(userId: string | null | undefined, storage: Pick<Storage, 'getItem'> | undefined = browserStorage()): ChoiceState {
  if (!userId) return DEFAULT_CHOICES
  try {
    const parsed = JSON.parse(storage?.getItem(choicesKey(userId)) ?? 'null') as Partial<ChoiceState> | null
    if (!parsed) return DEFAULT_CHOICES
    const presets = Array.isArray(parsed.presets) ? parsed.presets.filter((key): key is string => typeof key === 'string') : null
    const custom = Array.isArray(parsed.custom) ? parsed.custom.filter(isCustom) : []
    return { presets, custom }
  } catch { return DEFAULT_CHOICES }
}

/** Saves the choices for this account only; without a known account nothing is kept. */
export function writeChoices(userId: string | null | undefined, choices: ChoiceState,
  storage: Pick<Storage, 'setItem' | 'removeItem'> | undefined = browserStorage()) {
  if (!userId) return
  try {
    storage?.setItem(choicesKey(userId), JSON.stringify(choices))
    storage?.removeItem(LEGACY_KEY)
  } catch { /* private window or blocked storage: the choices last this visit */ }
}

/** Same solver settings; the name and description don't count, since the API would plan the same thing. */
export const sameCustom = (a: CustomStrategy, b: CustomStrategy) => a.allowMissingData === b.allowMissingData && a.allowAgronomicRisks === b.allowAgronomicRisks
  && a.allowSoilDepletion === b.allowSoilDepletion && a.irrigation === b.irrigation && a.fertilizer === b.fertilizer

/** Presets that will run, of those the API offers: all of them by default, or the chosen ones still on offer. */
export const chosenPresets = (choices: ChoiceState, available: readonly string[]) =>
  choices.presets === null ? [...available] : available.filter(key => choices.presets!.includes(key))

/** How many strategies the choices run. */
export const choiceCount = (choices: ChoiceState, available: readonly string[]) => chosenPresets(choices, available).length + choices.custom.length

/** Trimmed, with runs of whitespace as one space; undefined when blank. */
export const cleanText = (value: string | undefined) => value?.trim().replace(/\s+/g, ' ') || undefined

/** The strategy with its name and description tidied, and left out when blank so the API describes it from its settings. */
export function tidyCustom({ name, description, ...settings }: CustomStrategy): CustomStrategy {
  const ownName = cleanText(name), ownDescription = cleanText(description)
  return { ...settings, ...(ownName && { name: ownName }), ...(ownDescription && { description: ownDescription }) }
}

/** Whether a preset has exactly these settings; the custom strategy still runs as its own, beside the preset. */
export const presetMatches = (preset: StrategyPreset, custom: CustomStrategy) => {
  const settings = preset.settings
  return settings.allowMissingData === custom.allowMissingData && settings.allowAgronomicRisks === custom.allowAgronomicRisks
    && settings.allowSoilDepletion === custom.allowSoilDepletion && settings.irrigationLevel === custom.irrigation && settings.fertilizerLevel === custom.fertilizer
}

/**
 * The run request's strategies: undefined for the defaults, so the API picks its own presets; otherwise the chosen
 * presets in the API's order, then every custom one with its name and description.
 */
export function requestChoices(choices: ChoiceState, available: readonly string[]): StrategyChoice[] | undefined {
  if (choices.presets === null && !choices.custom.length) return undefined
  return [...chosenPresets(choices, available).map(preset => ({ preset })), ...choices.custom.map(tidyCustom)]
}

const settingWords = (prefix: string, level: ReplenishmentLevel) => `${prefix} ${level === 'OFF' ? 'off' : levelWords(level)}`

/** A custom strategy's settings in a few words, e.g. "Water in 3 steps, fertilizer in 4 steps". */
export const customTitle = (custom: CustomStrategy) => `${settingWords('Water', custom.irrigation)}, ${settingWords('fertilizer', custom.fertilizer)}`

/** The name the API gives a custom strategy without one, e.g. "Custom: water in 3 steps, fertilizer in 4 steps". */
export const defaultCustomName = (custom: CustomStrategy) =>
  `Custom: ${settingWords('water', custom.irrigation)}, ${settingWords('fertilizer', custom.fertilizer)}`

/** The description the API gives a custom strategy without one, worded like a preset's summary. */
export function defaultCustomDescription(custom: CustomStrategy): string {
  const water = custom.irrigation === 'OFF' ? 'Rain only' : `Water ${levelWords(custom.irrigation)}`
  const feed = custom.fertilizer === 'OFF' ? 'no fertilizer' : `fertilizer ${levelWords(custom.fertilizer)}`
  const risks = custom.allowAgronomicRisks ? 'Accepts agronomic risks' : 'Avoids every flagged risk'
  const soil = custom.allowSoilDepletion ? 'may draw the soil down' : 'keeps the soil fed'
  const data = custom.allowMissingData ? 'estimates missing figures' : 'plans only with complete figures'
  return `${water}, ${feed}. ${risks}, ${soil} and ${data}.`
}

/** The line beside Add strategy: why it is off, or what adding does. */
export function addHint({ full, duplicate, twin }: { full: boolean; duplicate: boolean; twin?: string }) {
  if (full) return `Up to ${MAX_STRATEGIES} plans are compared at a time. Remove one to add another.`
  if (duplicate) return 'You already have a plan with these settings.'
  if (twin) return `Same settings as the ${twin} preset. It is suggested as your own plan beside it.`
  return 'Adds these settings to your own plans above.'
}

export const customName = (custom: CustomStrategy) => cleanText(custom.name) ?? defaultCustomName(custom)
export const customDescription = (custom: CustomStrategy) => cleanText(custom.description) ?? defaultCustomDescription(custom)
