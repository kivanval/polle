import { describe, expect, it } from 'vitest'
import type { StrategyCycle } from './api'
import { agronomicRisks, cycleStages, expectedYield, ganttScale, hasMonthlyWater, nutrientFlow, seasonRainMm, settingsText, summarySteps,
  waterMonths, waterPlan } from './details'

const crop = (overrides: Partial<StrategyCycle>): StrategyCycle => ({
  cropProfileId: 1, cropId: 1, cropName: 'Wheat', systemName: 'Winter', plantingMonth: '2027-04', clearanceMonth: '2027-07',
  harvestMonths: ['2027-07'], revenueUsd: 0, costsUsd: 0, yieldPercent: 100, irrigationMm: 0, notes: [], ...overrides,
})

describe('summarySteps', () => {
  it('gives every crop one step in plan order, with its fertilizer months in order and totals', () => {
    const steps = summarySteps([
      crop({ revenueUsd: 900, costsUsd: 400, harvestMonths: ['2027-07', '2027-06'], fertilizer: { materialId: '3101', name: 'NPK 16-16-16', applications: [
        { month: '2027-06', nitrogenKgHa: 0, phosphorusKgHa: 5, potassiumKgHa: 0 },
        { month: '2027-04', nitrogenKgHa: 40.04, phosphorusKgHa: 15, potassiumKgHa: 10 },
      ] } }),
      crop({ cropName: 'Peas', fertilizer: null, revenueUsd: 100, costsUsd: 250 }),
      crop({ cropName: 'Barley' }),
    ])
    expect(steps.map(step => step.cycle.cropName)).toEqual(['Wheat', 'Peas', 'Barley'])
    expect(steps.map(step => step.netUsd)).toEqual([500, -150, 0])
    expect(steps[0].harvestMonths).toEqual(['2027-06', '2027-07'])
    // Only what the farmer buys: the product and its kilograms, no months or N-P-K figures.
    expect(steps[0].fertilizer).toEqual({ product: 'NPK 16-16-16', rateKgHa: null, targetPercent: null })
    expect(steps[1].fertilizer).toBeNull()
    expect(steps[2].fertilizer).toBeNull()
    expect(steps.map(step => step.water)).toEqual([null, null, null])
    expect(new Set(steps.map(step => step.key)).size).toBe(3)
  })

  it('keeps the kilograms of product per hectare and the share of the deficit it covers', () => {
    expect(summarySteps([crop({ fertilizer: { materialId: '3101', name: 'Nitrophoska', rateKgHa: 300, targetPercent: 75, applications: [] } })])[0].fertilizer)
      .toEqual({ product: 'Nitrophoska', rateKgHa: 300, targetPercent: 75 })
  })

  it('never shows a raw material id as the product name', () => {
    expect(summarySteps([crop({ fertilizer: { materialId: '77', name: null, applications: [] } })])[0].fertilizer?.product).toBe('Fertilizer')
    expect(summarySteps([crop({ fertilizer: { materialId: null, name: '  ', applications: [] } })])[0].fertilizer?.product).toBe('Fertilizer')
  })
})

describe('waterPlan', () => {
  it('takes the water the solver applied and its refill level', () => {
    expect(waterPlan({ irrigationMm: 132, irrigation: { materialId: '2215', amountMm: 140, targetPercent: 60 } })).toEqual({ mm: 132, targetPercent: 60 })
  })

  it('falls back to the allowance, and is null for a rain-fed crop', () => {
    expect(waterPlan({ irrigationMm: 0, irrigation: { materialId: null, amountMm: 80, targetPercent: null } })).toEqual({ mm: 80, targetPercent: null })
    expect(waterPlan({ irrigationMm: 0 })).toBeNull()
  })

  it('ignores a level outside 0-100', () => {
    expect(waterPlan({ irrigationMm: 50, irrigation: { materialId: null, amountMm: 50, targetPercent: 140 } })?.targetPercent).toBeNull()
  })
})

describe('expectedYield', () => {
  it('reads the solver yield as a share of the potential', () => {
    expect(expectedYield(87.6)).toBe('88% of potential')
    expect(expectedYield(100)).toBe('100% of potential')
    expect(expectedYield(104)).toBe('100% of potential')
    expect(expectedYield(undefined)).toBeNull()
    expect(expectedYield(Number.NaN)).toBeNull()
  })
})

describe('settingsText', () => {
  it('reads an older API without levels as on or off', () => {
    expect(settingsText({ irrigation: true, fertilizer: true, allowAgronomicRisks: true, allowSoilDepletion: true, allowMissingData: false }))
      .toEqual(['Water', 'Fertilizer', 'Accepts risks', 'May draw the soil down'])
    expect(settingsText({ irrigation: false, fertilizer: false, allowAgronomicRisks: false, allowSoilDepletion: false, allowMissingData: false }))
      .toEqual(['Rain only', 'No fertilizer', 'Avoids risks', 'Keeps the soil fed'])
  })

  it('names the dosing level and says when missing figures are estimated', () => {
    expect(settingsText({ irrigation: true, fertilizer: true, allowAgronomicRisks: true, allowSoilDepletion: true, allowMissingData: true,
      irrigationLevel: 'TERNARY', fertilizerLevel: 'CONTINUOUS' }))
      .toEqual(['Water in 3 steps', 'Fertilizer dosed finely', 'Accepts risks', 'May draw the soil down', 'Estimates missing figures'])
    expect(settingsText({ irrigation: false, fertilizer: true, allowAgronomicRisks: true, allowSoilDepletion: false, allowMissingData: false,
      irrigationLevel: 'OFF', fertilizerLevel: 'QUATERNARY' })[0]).toBe('Rain only')
  })
})

describe('cycleStages', () => {
  it('splits a winter crop into sowing, growth and harvest', () => {
    expect(cycleStages({ plantingMonth: '2026-10', clearanceMonth: '2027-07', harvestMonths: ['2027-07'] })).toEqual([
      { stage: 'sowing', from: '2026-10', to: '2026-10' },
      { stage: 'growth', from: '2026-11', to: '2027-06' },
      { stage: 'harvest', from: '2027-07', to: '2027-07' },
    ])
  })

  it('grows a perennial again between harvests and clears it after the last one', () => {
    expect(cycleStages({ plantingMonth: '2027-04', clearanceMonth: '2028-09', harvestMonths: ['2027-08', '2028-07'] }).map(span => `${span.stage} ${span.from}..${span.to}`))
      .toEqual(['sowing 2027-04..2027-04', 'growth 2027-05..2027-07', 'harvest 2027-08..2027-08', 'growth 2027-09..2028-06', 'harvest 2028-07..2028-09'])
  })

  it('keeps a one-month crop as sowing only', () => {
    expect(cycleStages({ plantingMonth: '2027-05', clearanceMonth: '2027-05', harvestMonths: ['2027-05'] })).toEqual([{ stage: 'sowing', from: '2027-05', to: '2027-05' }])
  })
})

describe('ganttScale', () => {
  it('lays expanded years out month by month and collapsed years as one narrow column', () => {
    const scale = ganttScale(2027, 2029, new Set([2028]), 30, 60)
    expect(scale.years.map(year => [year.year, year.x, year.width])).toEqual([[2027, 0, 60], [2028, 60, 360], [2029, 420, 60]])
    expect(scale.width).toBe(480)
    expect(scale.x('2028-03')).toBe(120)
    expect(scale.x('2028-03', true)).toBe(150)
    expect(scale.x('2027-07')).toBe(30)
    expect(scale.x('2029-12', true)).toBe(480)
  })

  it('clamps months outside the range to its edges', () => {
    const scale = ganttScale(2027, 2027, new Set(), 30, 60)
    expect(scale.x('2026-05')).toBe(0)
    expect(scale.x('2031-01')).toBe(60)
  })
})

describe('seasonRainMm', () => {
  it('adds the monthly normals from sowing through clearance, across the new year', () => {
    const rain = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120]
    expect(seasonRainMm({ plantingMonth: '2026-11', clearanceMonth: '2027-02' }, rain)).toBe(110 + 120 + 10 + 20)
  })
})

describe('waterMonths', () => {
  const rain = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120]
  const cycles = [
    { plantingMonth: '2027-04', clearanceMonth: '2027-05', monthlyWater: [{ month: '2027-04', irrigationMm: 0, waterUseMm: 30 }, { month: '2027-05', irrigationMm: 15, waterUseMm: 70 }] },
    { plantingMonth: '2027-07', clearanceMonth: '2027-07', monthlyWater: [{ month: '2027-07', irrigationMm: 5, waterUseMm: 90 }] },
  ]

  it('lists every month from first sowing to last clearance with rain, irrigation and crop use', () => {
    expect(waterMonths(cycles, rain)).toEqual([
      { month: '2027-04', rainMm: 40, irrigationMm: 0, useMm: 30, cropped: true },
      { month: '2027-05', rainMm: 50, irrigationMm: 15, useMm: 70, cropped: true },
      { month: '2027-06', rainMm: 60, irrigationMm: 0, useMm: 0, cropped: false },
      { month: '2027-07', rainMm: 70, irrigationMm: 5, useMm: 90, cropped: true },
    ])
  })

  it('spans the whole planning horizon, with bare months before and after the crops', () => {
    const months = waterMonths(cycles, rain, { startMonth: '2027-03', endMonth: '2027-09' })
    expect(months.map(month => month.month)).toEqual(['2027-03', '2027-04', '2027-05', '2027-06', '2027-07', '2027-08', '2027-09'])
    expect(months[0]).toEqual({ month: '2027-03', rainMm: 30, irrigationMm: 0, useMm: 0, cropped: false })
    expect(months.at(-1)).toEqual({ month: '2027-09', rainMm: 90, irrigationMm: 0, useMm: 0, cropped: false })
  })

  it('leaves rain unknown until the field rainfall loads, and reads plans without monthly figures as having none', () => {
    expect(waterMonths(cycles)[0].rainMm).toBeNull()
    expect(hasMonthlyWater(cycles)).toBe(true)
    expect(hasMonthlyWater([{ monthlyWater: [] }, {}])).toBe(false)
  })
})

describe('nutrientFlow', () => {
  const nutrients = { nitrogenAppliedKgHa: 45, nitrogenFixedKgHa: 80, nitrogenRemovedKgHa: 110, phosphorusAppliedKgHa: 20, phosphorusRemovedKgHa: 30,
    potassiumAppliedKgHa: 10, potassiumRemovedKgHa: 55 }

  it('reads nitrogen with what legumes fix, and phosphorus and potassium without', () => {
    expect(nutrientFlow(nutrients, 'N')).toEqual({ applied: 45, fixed: 80, removed: 110 })
    expect(nutrientFlow(nutrients, 'P')).toEqual({ applied: 20, fixed: 0, removed: 30 })
    expect(nutrientFlow(nutrients, 'K')).toEqual({ applied: 10, fixed: 0, removed: 55 })
  })

  it('reads an older result without nutrients as zero', () => {
    expect(nutrientFlow(undefined, 'K')).toEqual({ applied: 0, fixed: 0, removed: 0 })
  })
})

describe('the current plan audit', () => {
  it('lists each crop with its agronomic risks and warnings, leaving out crops without any', () => {
    const crops = agronomicRisks([
      crop({ cropName: 'Wheat', risks: ['Drought: rain covers only 60%', 'Crop rotation: Wheat comes back after 2 months'], warnings: [] }),
      crop({ cropName: 'Peas', risks: [], warnings: [] }),
      crop({ cropName: 'Sunflower', risks: [], warnings: ['Moisture deficit: short of 40 mm'] }),
    ])
    expect(crops.map(item => item.cropName)).toEqual(['Wheat', 'Sunflower'])
    expect(crops[0].risks).toHaveLength(2)
    expect(crops[1].warnings).toEqual(['Moisture deficit: short of 40 mm'])
  })
})
