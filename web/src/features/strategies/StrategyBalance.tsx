import type { StrategyCycle } from './api'
import { useFieldRainfall } from './api'
import { hasMonthlyWater, NUTRIENT_LABEL, nutrientFlow, seasonRainMm, waterMonths, type Nutrient, type WaterHorizon } from './details'
import { cubicMetres, monthText, whenText } from './format'

const mm = new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 })
const kg = (value: number) => `${mm.format(value)} kg/ha`
const signedKg = (value: number) => `${Math.round(value) > 0 ? '+' : Math.round(value) < 0 ? '−' : '±'}${kg(Math.abs(value))}`
const percent = (part: number, whole: number) => whole > 0 ? (part / whole) * 100 : 0

const cropKey = (cycle: StrategyCycle, index: number) => `${cycle.plantingMonth}-${cycle.cropProfileId}-${index}`
const CropLabel = ({ cycle }: { cycle: StrategyCycle }) =>
  <span className="balance-crop"><strong>{cycle.cropName}</strong><span>{`${whenText(cycle.plantingDate, cycle.plantingMonth)} – ${whenText(cycle.clearanceDate, cycle.clearanceMonth)}`}</span></span>

/**
 * The plan's water: month by month when the solver reported crop water use (water in against water used), otherwise per
 * crop (rain and irrigation only), for strategies prepared before it did.
 */
export function WaterBalanceChart({ fieldId, cycles, areaHectares, horizon }: { fieldId: string; cycles: StrategyCycle[]; areaHectares: number; horizon?: WaterHorizon }) {
  const rainfall = useFieldRainfall(fieldId)
  return hasMonthlyWater(cycles)
    ? <MonthlyWaterChart rainfall={rainfall} cycles={cycles} areaHectares={areaHectares} horizon={horizon} />
    : <SeasonWaterChart rainfall={rainfall} cycles={cycles} areaHectares={areaHectares} />
}

type Rainfall = ReturnType<typeof useFieldRainfall>
const PLOT_TICKS = [1, 0.5]
/** Height above the chart's bottom of a share of the plot; the plot sits on the 16px row of year labels. */
const plotHeight = (share: number) => `calc(16px + ${share} * (100% - 16px))`

/**
 * Water in (the field's normal rain, plus the strategy's irrigation) against what the crops used, month by month, on one
 * scale, over the whole planning horizon. Bare months show their rain only: the solver reports no use for a bare field.
 */
function MonthlyWaterChart({ rainfall, cycles, areaHectares, horizon }: { rainfall: Rainfall; cycles: StrategyCycle[]; areaHectares: number; horizon?: WaterHorizon }) {
  const months = waterMonths(cycles, rainfall.data?.monthlyRainMm, horizon)
  const allRain = months.reduce((sum, month) => sum + (month.rainMm ?? 0), 0)
  const max = Math.max(1, ...months.map(month => Math.max((month.rainMm ?? 0) + month.irrigationMm, month.useMm)))
  const top = niceCeiling(max)
  const cropped = months.filter(month => month.cropped)
  const rain = cropped.reduce((sum, month) => sum + (month.rainMm ?? 0), 0)
  const irrigation = months.reduce((sum, month) => sum + month.irrigationMm, 0)
  const use = months.reduce((sum, month) => sum + month.useMm, 0)
  const known = rainfall.data !== undefined

  return <section className="balance-panel balance-panel-wide" aria-labelledby="water-balance-title">
    <h3 id="water-balance-title">Water balance by month</h3>
    <dl className="balance-totals">
      <div><dt><span className="balance-swatch is-rain" aria-hidden="true" />{`Rain, ${months.length} months`}</dt><dd className="mono">{known ? `${mm.format(allRain)} mm` : '—'}</dd></div>
      <div><dt><span className="balance-swatch is-rain" aria-hidden="true" />Rain while crops grow</dt><dd className="mono">{known ? `${mm.format(rain)} mm` : '—'}</dd></div>
      <div><dt><span className="balance-swatch is-irrigation" aria-hidden="true" />Irrigation</dt><dd className="mono">{`${mm.format(irrigation)} mm · ${cubicMetres(irrigation, areaHectares)}`}</dd></div>
      <div><dt><span className="balance-swatch is-use" aria-hidden="true" />Crop water use</dt><dd className="mono">{`${mm.format(use)} mm`}</dd></div>
      {known && <div><dt>In minus use</dt><dd className={`mono ${rain + irrigation - use < 0 ? 'is-bad' : ''}`}>{signedMm(rain + irrigation - use)}</dd></div>}
    </dl>
    {rainfall.isPending && <p className="strategy-note" role="status">Loading the field's rainfall…</p>}
    {rainfall.isError && <p className="strategy-note" role="status">Rainfall for this field isn't available right now, so water in shows irrigation only.</p>}
    <div className="water-chart">
      <div className="water-axis mono" aria-hidden="true">{[...PLOT_TICKS, 0].map(tick => <span key={tick} style={{ bottom: plotHeight(tick) }}>{mm.format(top * tick)}</span>)}</div>
      <div className="water-scroll">
        <ol className="water-months" aria-label="Water in and used by month">
          {PLOT_TICKS.map(tick => <li key={`grid-${tick}`} className="water-grid" style={{ bottom: plotHeight(tick) }} aria-hidden="true" />)}
          {months.map(month => {
            const inflow = (month.rainMm ?? 0) + month.irrigationMm
            const text = `${monthText(month.month)}${month.cropped ? '' : ' (bare field)'}: ${month.rainMm === null ? '' : `rain ${mm.format(month.rainMm)} mm, `}irrigation ${mm.format(month.irrigationMm)} mm, crop use ${mm.format(month.useMm)} mm`
            const january = month.month.endsWith('-01') || month === months[0]
            return <li key={month.month} className={`water-month ${month.cropped ? '' : 'is-bare'} ${january ? 'is-year-start' : ''}`} title={text} aria-label={text}>
              <span className="water-bars">
                <span className="water-bar is-in" style={{ height: `${percent(inflow, top)}%` }}>
                  {month.irrigationMm > 0 && <span className="balance-segment is-irrigation" style={{ height: `${percent(month.irrigationMm, inflow)}%` }} />}
                  {(month.rainMm ?? 0) > 0 && <span className="balance-segment is-rain" style={{ height: `${percent(month.rainMm ?? 0, inflow)}%` }} />}
                </span>
                <span className="water-bar is-use balance-segment" style={{ height: `${percent(month.useMm, top)}%` }} />
              </span>
              <span className="water-label mono" aria-hidden="true">{january ? month.month.slice(0, 4) : ''}</span>
            </li>
          })}
        </ol>
      </div>
    </div>
    <div className="balance-legend">
      <span><span className="balance-swatch is-rain" aria-hidden="true" />Rain</span>
      <span><span className="balance-swatch is-irrigation" aria-hidden="true" />Irrigation</span>
      <span><span className="balance-swatch is-use" aria-hidden="true" />Used by the crop</span>
      <span><span className="balance-swatch is-bare" aria-hidden="true" />No crop on the field</span>
    </div>
    <p className="strategy-note">{`Left bar: water in. Right bar: water the crop used (evapotranspiration), in mm. ${rainfall.data
      ? `Rain is the field's average for each month over ${rainfall.data.referenceStartYear}–${rainfall.data.referenceEndYear} (NASA POWER). Not all of it reaches the roots. `
      : ''}1 mm over 1 ha is 10 m³.`}</p>
  </section>
}

/** A round axis top just above the largest value: 1, 2 or 5 times a power of ten. */
function niceCeiling(value: number) {
  const power = 10 ** Math.floor(Math.log10(value))
  return ([1, 2, 5, 10].map(step => step * power).find(candidate => candidate >= value) ?? 10 * power)
}

const signedMm = (value: number) => `${Math.round(value) > 0 ? '+' : Math.round(value) < 0 ? '−' : '±'}${mm.format(Math.abs(value))} mm`

/**
 * Water each crop gets over its season: the rain the field typically has in those months (the ten-year baseline the
 * planner uses) and the irrigation the strategy adds, stacked on one scale.
 */
function SeasonWaterChart({ rainfall, cycles, areaHectares }: { rainfall: Rainfall; cycles: StrategyCycle[]; areaHectares: number }) {
  const monthly = rainfall.data?.monthlyRainMm
  const rows = cycles.map(cycle => ({ cycle, rain: monthly ? seasonRainMm(cycle, monthly) : 0, irrigation: cycle.irrigationMm }))
  const max = Math.max(1, ...rows.map(row => row.rain + row.irrigation))
  const rain = rows.reduce((sum, row) => sum + row.rain, 0), irrigation = rows.reduce((sum, row) => sum + row.irrigation, 0)

  return <section className="balance-panel" aria-labelledby="water-balance-title">
    <h3 id="water-balance-title">Water per crop</h3>
    <dl className="balance-totals">
      <div><dt><span className="balance-swatch is-rain" aria-hidden="true" />Rain in season</dt><dd className="mono">{monthly ? `${mm.format(rain)} mm` : '—'}</dd></div>
      <div><dt><span className="balance-swatch is-irrigation" aria-hidden="true" />Irrigation</dt><dd className="mono">{`${mm.format(irrigation)} mm · ${cubicMetres(irrigation, areaHectares)}`}</dd></div>
      {monthly && <div><dt>Rain's share</dt><dd className="mono">{`${Math.round(percent(rain, rain + irrigation))}%`}</dd></div>}
    </dl>
    {rainfall.isPending && <p className="strategy-note" role="status">Loading the field's rainfall…</p>}
    {rainfall.isError && <p className="strategy-note" role="status">Rainfall for this field isn't available right now, so only irrigation is shown.</p>}
    <ul className="balance-rows">{rows.map((row, index) => {
      const text = `${row.cycle.cropName}: ${monthly ? `rain ${mm.format(row.rain)} mm, ` : ''}irrigation ${mm.format(row.irrigation)} mm (${cubicMetres(row.irrigation, areaHectares)})`
      return <li key={cropKey(row.cycle, index)} className="balance-row">
        <CropLabel cycle={row.cycle} />
        <span className="balance-bar" role="img" aria-label={text} title={text}>
          <span className="balance-fill">
            {row.rain > 0 && <span className="balance-segment is-rain" style={{ width: `${percent(row.rain, max)}%` }} />}
            {row.irrigation > 0 && <span className="balance-segment is-irrigation" style={{ width: `${percent(row.irrigation, max)}%` }} />}
          </span>
          <span className="balance-value mono">{`${mm.format(row.rain + row.irrigation)} mm`}</span>
        </span>
      </li>
    })}</ul>
    <p className="strategy-note">{`The planner didn't report water use by month for these crops, so each shows its season's rain and irrigation only. `}{rainfall.data
      ? `Rain is the field's average for those months over ${rainfall.data.referenceStartYear}–${rainfall.data.referenceEndYear} (NASA POWER), the same the planner uses. 1 mm over 1 ha is 10 m³.`
      : '1 mm over 1 ha is 10 m³.'}</p>
  </section>
}

const NUTRIENTS: Nutrient[] = ['N', 'P', 'K']

/**
 * Nitrogen, phosphorus and potassium side by side, one block each in its own colour (N blue, P orange, K aqua). Per crop,
 * a diverging bar: what the harvest takes off to the left in a light tint, what fertilizer adds to the right in the full
 * colour, and for nitrogen what legumes fix, striped. Each block heads with the plan's net balance for that nutrient.
 */
export function NutrientChart({ cycles, wide = false }: { cycles: StrategyCycle[]; wide?: boolean }) {
  const panel = `balance-panel ${wide ? 'balance-panel-wide' : ''}`
  if (cycles.length && cycles.every(cycle => !cycle.nutrients)) return <section className={panel} aria-labelledby="nutrient-title">
    <h3 id="nutrient-title">Nutrients per crop (N-P-K)</h3>
    <p className="strategy-note">This strategy was prepared before nutrient figures were recorded. Suggest strategies again to see them.</p>
  </section>

  return <section className={panel} aria-labelledby="nutrient-title">
    <h3 id="nutrient-title">Nutrients per crop (N-P-K)</h3>
    <div className="balance-legend">
      <span><span className="balance-swatch is-key-removed" aria-hidden="true" />Light, left: taken off with the harvest</span>
      <span><span className="balance-swatch is-key-applied" aria-hidden="true" />Solid, right: fertilizer</span>
      <span><span className="balance-swatch is-key-fixed" aria-hidden="true" />Striped: fixed by legumes (N only)</span>
    </div>
    <div className="nutrient-panels">{NUTRIENTS.map(nutrient => {
      const flows = cycles.map(cycle => nutrientFlow(cycle.nutrients, nutrient))
      const max = Math.max(1, ...flows.map(flow => Math.max(flow.removed, flow.applied + flow.fixed)))
      const net = flows.reduce((sum, flow) => sum + flow.applied + flow.fixed - flow.removed, 0)
      return <div key={nutrient} className={`nutrient-panel is-${nutrient.toLowerCase()}`}>
        <h4><span className="nutrient-name"><span className="nutrient-dot" aria-hidden="true" />{NUTRIENT_LABEL[nutrient]}</span>
          <span className={`mono ${Math.round(net) < 0 ? 'is-bad' : ''}`} title="Added minus taken off over the whole plan">{`Net ${signedKg(net)}`}</span></h4>
        <ul className="balance-rows">{cycles.map((cycle, index) => {
          const flow = flows[index]
          const text = `${cycle.cropName}, ${NUTRIENT_LABEL[nutrient]}: fertilizer ${kg(flow.applied)}${nutrient === 'N' ? `, fixed ${kg(flow.fixed)}` : ''}, taken off ${kg(flow.removed)}`
          return <li key={cropKey(cycle, index)} className="nutrient-row">
            <span className="nutrient-crop">{cycle.cropName}</span>
            <span className="nutrient-bar" role="img" aria-label={text} title={text}>
              <span className="nutrient-value mono">{flow.removed ? `−${mm.format(flow.removed)}` : ''}</span>
              <span className="nutrient-fill is-out">
                {flow.removed > 0 && <span className="balance-segment is-removed" style={{ width: `${percent(flow.removed, max)}%` }} />}
              </span>
              <span className="nutrient-fill is-in">
                {flow.applied > 0 && <span className="balance-segment is-applied" style={{ width: `${percent(flow.applied, max)}%` }} />}
                {flow.fixed > 0 && <span className="balance-segment is-fixed" style={{ width: `${percent(flow.fixed, max)}%` }} />}
              </span>
              <span className="nutrient-value mono">{flow.applied + flow.fixed ? `+${mm.format(flow.applied + flow.fixed)}` : ''}</span>
            </span>
          </li>
        })}</ul>
      </div>
    })}</div>
    <p className="strategy-note">Kilograms per hectare over each crop's season.</p>
  </section>
}
