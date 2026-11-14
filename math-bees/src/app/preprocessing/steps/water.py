from __future__ import annotations

from dataclasses import dataclass, replace
from datetime import date, timedelta
from typing import Final

import numpy as np
from numba import njit

from app.models.agronomy import CropProfile, CropProfileStage
from app.models.fao56 import KC_BARE_SOIL
from app.models.planning import SolverContext
from app.models.types import (
    DeathNote,
    MonthlyWaterDynamics,
    Note,
    NoteCategory,
    NoteImportance,
    StageCategory,
    WaterMm,
    YieldMultiplier,
)
from app.models.year_month import YearMonth
from app.preprocessing.candidate import SimulationCandidate
from app.preprocessing.steps.base import SimulationStep

DEFAULT_DROUGHT_TOLERANCE_DAYS: Final[int] = 0
FALLBACK_GENERIC_KC: Final[float] = 1.0
DEFAULT_WATER_YIELD_RESPONSE_KY: Final[float] = 1.0
DEFAULT_BASE_YIELD_MULTIPLIER: Final[float] = 1.0
INITIAL_CYCLE_MULTIPLIER: Final[float] = 1.0
MIN_DEFICIT_RATIO: Final[float] = 0.0
MIN_STAGE_FACTOR: Final[float] = 0.0
SEVERE_STRESS_KS_THRESHOLD: Final[float] = 0.4
WATER_BALANCE_ROUND_DECIMALS: Final[int] = 2
YIELD_MULTIPLIER_ROUND_DECIMALS: Final[int] = 3


@dataclass(slots=True)
class _TimeMappings:
    month_starts: np.ndarray
    month_yms: tuple[YearMonth, ...]


@dataclass(slots=True)
class _ParsedStageInfo:
    kc_val: float
    category: StageCategory
    label: str
    tolerance: int
    ky: float
    risk_note: Note | None


@dataclass(slots=True)
class _ProfileStagesCache:
    executed_stages: list[CropProfileStage]
    parsed_info: list[_ParsedStageInfo]


@dataclass(slots=True)
class _PreparedWaterInputs:
    total_days: int
    taw_mm: float
    raw_mm: float
    parsed_stages: list[_ParsedStageInfo]
    irr_eligible_mask: np.ndarray
    precip_slice: np.ndarray
    potential_etc: np.ndarray
    mappings: _TimeMappings


def _build_executed_stages(profile: CropProfile, num_segments: int) -> list[CropProfileStage]:
    stages = profile.stages
    if not stages:
        return []

    if num_segments <= len(stages):
        return list(stages[:num_segments])

    if not profile.is_perennial:
        return list(stages)

    restart_idx = 0
    if profile.perennial_restart_stage is not None:
        for idx, s in enumerate(stages):
            if s.sequence_order == profile.perennial_restart_stage:
                restart_idx = idx
                break

    result = list(stages)
    curr_idx = restart_idx

    while len(result) < num_segments:
        result.append(stages[curr_idx])
        curr_idx = (curr_idx + 1) if (curr_idx + 1) < len(stages) else restart_idx

    return result


@njit
def _simulate_daily_bucket_and_stress(
    total_days: int,
    taw: float,
    raw: float,
    irr_target: float,
    potential_etc: np.ndarray,
    precip: np.ndarray,
    irr_eligible_mask: np.ndarray,
    ks_threshold: float,
) -> tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
    irr_applied = np.zeros(total_days, dtype=np.float32)
    actual_etc = np.zeros(total_days, dtype=np.float32)
    daily_ks = np.ones(total_days, dtype=np.float32)
    is_stressed = np.zeros(total_days, dtype=np.bool_)

    current_moisture = taw

    for i in range(total_days):
        rain = precip[i]
        pot_etc = potential_etc[i]

        current_moisture += rain
        if current_moisture > taw:
            current_moisture = taw

        if (
            irr_target > 0.0
            and irr_eligible_mask[i]
            and pot_etc > 0
            and current_moisture < taw - raw
        ):
            target_moisture = (taw - raw) + (raw * irr_target)
            needed = target_moisture - current_moisture
            if needed > 0:
                irr_applied[i] = needed
                current_moisture += needed

        if current_moisture >= pot_etc:
            act = pot_etc
            current_moisture -= pot_etc
        else:
            act = current_moisture
            current_moisture = 0.0

        actual_etc[i] = act

        if pot_etc > 0:
            ks = act / pot_etc
            daily_ks[i] = ks
            if ks < ks_threshold:
                is_stressed[i] = True

    return irr_applied, actual_etc, daily_ks, is_stressed


class SoilWaterBucketStep(SimulationStep):
    def __init__(self) -> None:
        self._time_map_cache: dict[tuple[date, int], _TimeMappings] = {}
        self._profile_stages_cache: dict[tuple[object, int], _ProfileStagesCache] = {}

    def _get_time_mappings(self, plant_date: date, days_count: int) -> _TimeMappings:
        cache_key = (plant_date, days_count)
        cached = self._time_map_cache.get(cache_key)
        if cached is not None:
            return cached

        month_starts_list = [0]
        month_yms_list = [YearMonth(plant_date.year, plant_date.month)]

        curr_date = plant_date
        curr_m = curr_date.month

        for i in range(1, days_count):
            curr_date += timedelta(days=1)
            is_new_month = curr_date.month != curr_m

            if is_new_month:
                month_starts_list.append(i)
                curr_m = curr_date.month
                month_yms_list.append(YearMonth(curr_date.year, curr_date.month))

        month_starts = np.array(month_starts_list, dtype=np.int32)
        mappings = _TimeMappings(
            month_starts=month_starts,
            month_yms=tuple(month_yms_list),
        )
        self._time_map_cache[cache_key] = mappings
        return mappings

    def _get_profile_stage_info(
        self,
        profile: CropProfile,
        num_segments: int,
    ) -> _ProfileStagesCache:
        cache_key = (profile.id, num_segments)
        cached = self._profile_stages_cache.get(cache_key)
        if cached is not None:
            return cached

        executed = _build_executed_stages(profile, num_segments)
        parsed: list[_ParsedStageInfo] = []

        for stage in executed:
            risk_note = None
            if stage.water_crop_coefficient is None:
                if stage.category == StageCategory.DORMANCY:
                    kc_val = KC_BARE_SOIL
                else:
                    risk_note = Note(
                        NoteCategory.DATA_MISSING,
                        NoteImportance.WARN,
                        f"Missing Kc coefficient: {stage.label}",
                    )
                    kc_val = FALLBACK_GENERIC_KC
            else:
                kc_val = float(stage.water_crop_coefficient.real)

            tolerance = (
                stage.drought_shock_tolerance_days
                if stage.drought_shock_tolerance_days is not None
                else DEFAULT_DROUGHT_TOLERANCE_DAYS
            )
            ky = (
                float(stage.water_yield_response_factor.real)
                if stage.water_yield_response_factor is not None
                else DEFAULT_WATER_YIELD_RESPONSE_KY
            )

            parsed.append(
                _ParsedStageInfo(
                    kc_val=kc_val,
                    category=stage.category,
                    label=stage.label,
                    tolerance=tolerance,
                    ky=ky,
                    risk_note=risk_note,
                ),
            )

        info = _ProfileStagesCache(executed_stages=executed, parsed_info=parsed)
        self._profile_stages_cache[cache_key] = info
        return info

    def _prepare_inputs(
        self,
        candidate: SimulationCandidate,
        context: SolverContext,
        *,
        abort_on_fatal: bool = True,
    ) -> tuple[SimulationCandidate, _PreparedWaterInputs | None]:
        if candidate.is_dead or not candidate.stage_offsets:
            return candidate, None

        total_days = int(candidate.stage_offsets[-1].real)
        if len(candidate.et0s) < total_days or len(candidate.precipitations) < total_days:
            cand = candidate.with_risk(
                Note(
                    NoteCategory.DATA_MISSING,
                    NoteImportance.RISK,
                    f"Insufficient weather data: requires {total_days} days",
                ),
                fatal_if=abort_on_fatal,
                death_note=DeathNote("SoilWaterBucket:MissingWeatherData", is_static=False),
            )
            return cand, None

        soil = candidate.field.soil
        if soil is None or soil.hydraulics is None:
            cand = candidate.with_risk(
                Note(
                    NoteCategory.DATA_MISSING,
                    NoteImportance.RISK,
                    "Missing soil hydraulics (Total Available Water) data",
                ),
                fatal_if=abort_on_fatal and not context.risk.allow_missing_data,
                death_note=DeathNote("SoilWaterBucket:MissingTAW", is_static=True),
            )
            return cand, None

        taw_mm = float(soil.hydraulics.total_available_water_mm.real)

        if candidate.profile.moisture_depletion_fraction is None:
            cand = candidate.with_risk(
                Note(
                    NoteCategory.DATA_MISSING,
                    NoteImportance.RISK,
                    f"Missing moisture depletion fraction for profile",
                ),
                fatal_if=abort_on_fatal and not context.risk.allow_missing_data,
                death_note=DeathNote("SoilWaterBucket:MissingDepletionFraction", is_static=True),
            )
            return cand, None

        raw_mm = taw_mm * float(candidate.profile.moisture_depletion_fraction.real)

        stage_cache = self._get_profile_stage_info(candidate.profile, len(candidate.stage_offsets))
        parsed_stages = stage_cache.parsed_info

        curr_cand = candidate
        kc_array = np.empty(total_days, dtype=np.float32)
        irr_eligible_mask = np.ones(total_days, dtype=bool)
        start_idx = 0

        for s_info, offset in zip(parsed_stages, candidate.stage_offsets, strict=False):
            end_idx = min(int(offset.real), total_days)
            if start_idx >= end_idx:
                continue
            if s_info.risk_note is not None:
                curr_cand = curr_cand.with_risk(s_info.risk_note)

            kc_array[start_idx:end_idx] = s_info.kc_val

            if s_info.category in (StageCategory.DORMANCY, StageCategory.HARVEST):
                irr_eligible_mask[start_idx:end_idx] = False

            start_idx = end_idx

        et0_slice = curr_cand.et0s[:total_days]
        precip_slice = curr_cand.precipitations[:total_days]
        potential_etc = kc_array * et0_slice
        mappings = self._get_time_mappings(curr_cand.plant_date, total_days)

        inputs = _PreparedWaterInputs(
            total_days=total_days,
            taw_mm=taw_mm,
            raw_mm=raw_mm,
            parsed_stages=parsed_stages,
            irr_eligible_mask=irr_eligible_mask,
            precip_slice=precip_slice,
            potential_etc=potential_etc,
            mappings=mappings,
        )
        return curr_cand, inputs

    @staticmethod
    def _evaluate_trajectory(
        curr_cand: SimulationCandidate,
        context: SolverContext,
        inputs: _PreparedWaterInputs,
        irr_applied: np.ndarray,
        actual_etc: np.ndarray,
        is_stressed: np.ndarray,
        *,
        abort_on_fatal: bool = True,
    ) -> SimulationCandidate:
        total_days = inputs.total_days
        parsed_stages = inputs.parsed_stages
        death_cand = curr_cand

        if np.any(is_stressed):
            start_idx = 0
            for s_info, offset in zip(parsed_stages, curr_cand.stage_offsets, strict=False):
                end_idx = min(int(offset.real), total_days)
                if start_idx >= end_idx or s_info.category == StageCategory.DORMANCY:
                    start_idx = end_idx
                    continue

                stage_stress = is_stressed[start_idx:end_idx]
                tolerance = s_info.tolerance

                if np.count_nonzero(stage_stress) > tolerance:
                    curr_consecutive = 0
                    max_consecutive = 0
                    for val in stage_stress:
                        if val:
                            curr_consecutive += 1
                            if curr_consecutive > tolerance:
                                max_consecutive = curr_consecutive
                                break
                        else:
                            curr_consecutive = 0

                    if max_consecutive > tolerance:
                        death_cand = death_cand.with_risk(
                            Note(
                                NoteCategory.AGRONOMIC,
                                NoteImportance.RISK,
                                f"Severe drought shock: {s_info.label} ({max_consecutive}d > tolerance {tolerance}d)",
                            ),
                            fatal_if=abort_on_fatal and not context.risk.allow_agronomic_risks,
                            death_note=DeathNote(
                                f"SoilWaterBucket:DroughtShockExceeded({s_info.label})",
                                is_static=False,
                            ),
                        )
                        if death_cand.is_dead:
                            break
                start_idx = end_idx

        if death_cand.is_dead:
            return death_cand

        cum_pot_etc = np.empty(total_days + 1, dtype=np.float32)
        cum_pot_etc[0] = 0.0
        np.cumsum(inputs.potential_etc, out=cum_pot_etc[1:])

        cum_act_etc = np.empty(total_days + 1, dtype=np.float32)
        cum_act_etc[0] = 0.0
        np.cumsum(actual_etc, out=cum_act_etc[1:])

        multipliers: list[YieldMultiplier] = []
        current_cycle_mult = INITIAL_CYCLE_MULTIPLIER
        st_idx = 0
        h_idx = 0
        existing_mults = curr_cand.yield_multipliers

        for s_info, offset in zip(parsed_stages, curr_cand.stage_offsets, strict=False):
            end_idx = min(int(offset.real), total_days)
            if st_idx >= end_idx or s_info.category == StageCategory.DORMANCY:
                st_idx = end_idx
                continue

            stage_pot = float(cum_pot_etc[end_idx] - cum_pot_etc[st_idx])
            stage_act = float(cum_act_etc[end_idx] - cum_act_etc[st_idx])
            ky = s_info.ky

            if stage_pot > 0.0:
                deficit_ratio = max(MIN_DEFICIT_RATIO, 1.0 - (stage_act / stage_pot))
                stage_factor = max(MIN_STAGE_FACTOR, 1.0 - ky * deficit_ratio)
            else:
                stage_factor = 1.0

            current_cycle_mult *= stage_factor
            st_idx = end_idx

            if s_info.category == StageCategory.HARVEST:
                if h_idx < len(existing_mults):
                    base_mult = float(existing_mults[h_idx].real)
                elif existing_mults:
                    base_mult = float(existing_mults[-1].real)
                else:
                    base_mult = DEFAULT_BASE_YIELD_MULTIPLIER

                multipliers.append(
                    YieldMultiplier(
                        round(base_mult * current_cycle_mult, YIELD_MULTIPLIER_ROUND_DECIMALS),
                    ),
                )
                current_cycle_mult = INITIAL_CYCLE_MULTIPLIER
                h_idx += 1

        if not multipliers:
            base_mult = (
                float(existing_mults[-1].real) if existing_mults else DEFAULT_BASE_YIELD_MULTIPLIER
            )
            multipliers.append(
                YieldMultiplier(
                    round(base_mult * current_cycle_mult, YIELD_MULTIPLIER_ROUND_DECIMALS),
                ),
            )
            h_idx += 1

        while h_idx < len(existing_mults):
            multipliers.append(existing_mults[h_idx])
            h_idx += 1

        month_starts = inputs.mappings.month_starts
        monthly_irr_raw = np.add.reduceat(irr_applied, month_starts)
        net_water = actual_etc - (inputs.precip_slice + irr_applied)
        monthly_net = np.add.reduceat(net_water, month_starts)
        monthly_act_etc = np.add.reduceat(actual_etc, month_starts)

        water_dynamics: dict[YearMonth, MonthlyWaterDynamics] = {}
        for ym, m_irr, m_net, m_etc in zip(
            inputs.mappings.month_yms, monthly_irr_raw, monthly_net, monthly_act_etc, strict=False
        ):
            water_dynamics[ym] = MonthlyWaterDynamics(
                applied_mm=WaterMm(round(float(m_irr), WATER_BALANCE_ROUND_DECIMALS)),
                removed_mm=WaterMm(round(float(m_etc), WATER_BALANCE_ROUND_DECIMALS)),
                net_balance_mm=WaterMm(round(float(m_net), WATER_BALANCE_ROUND_DECIMALS)),
            )

        return replace(
            death_cand,
            monthly_water=water_dynamics,
            yield_multipliers=tuple(multipliers),
        )

    def apply(
        self,
        candidate: SimulationCandidate,
        context: SolverContext,
    ) -> list[SimulationCandidate]:
        curr_cand, inputs = self._prepare_inputs(candidate, context, abort_on_fatal=True)
        if inputs is None:
            return [curr_cand]

        irrigation = context.replenishment.irrigation
        irr_levels = irrigation.levels

        generated_variants: list[SimulationCandidate] = []
        seen_fingerprints: set[tuple[float, ...]] = set()

        for irr_target in irr_levels:
            if not irrigation.apply:
                irr_target = 0.0

            irr_applied, actual_etc, _daily_ks, is_stressed = _simulate_daily_bucket_and_stress(
                total_days=inputs.total_days,
                taw=inputs.taw_mm,
                raw=inputs.raw_mm,
                irr_target=float(irr_target),
                potential_etc=inputs.potential_etc,
                precip=inputs.precip_slice,
                irr_eligible_mask=inputs.irr_eligible_mask,
                ks_threshold=SEVERE_STRESS_KS_THRESHOLD,
            )

            total_irr = round(float(np.sum(irr_applied)), WATER_BALANCE_ROUND_DECIMALS)
            variant = self._evaluate_trajectory(
                curr_cand=curr_cand,
                context=context,
                inputs=inputs,
                irr_applied=irr_applied,
                actual_etc=actual_etc,
                is_stressed=is_stressed,
                abort_on_fatal=True,
            )

            if variant.is_dead:
                generated_variants.append(variant)
                continue

            fingerprint = (total_irr, *(m.real for m in variant.yield_multipliers))
            if fingerprint in seen_fingerprints:
                continue
            seen_fingerprints.add(fingerprint)

            generated_variants.append(variant)

        return generated_variants


@njit
def _simulate_capped_irrigation_bucket_and_stress(
    total_days: int,
    taw: float,
    raw: float,
    max_irr_mm: float,
    potential_etc: np.ndarray,
    precip: np.ndarray,
    irr_eligible_mask: np.ndarray,
    ks_threshold: float,
) -> tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
    irr_applied = np.zeros(total_days, dtype=np.float32)
    actual_etc = np.zeros(total_days, dtype=np.float32)
    daily_ks = np.ones(total_days, dtype=np.float32)
    is_stressed = np.zeros(total_days, dtype=np.bool_)

    current_moisture = taw
    remaining_irr = max_irr_mm

    for i in range(total_days):
        rain = precip[i]
        pot_etc = potential_etc[i]

        current_moisture += rain
        if current_moisture > taw:
            current_moisture = taw

        if (
            remaining_irr > 0.0
            and irr_eligible_mask[i]
            and pot_etc > 0
            and current_moisture < taw - raw
        ):
            needed = taw - current_moisture
            if needed > 0:
                applied = needed if needed <= remaining_irr else remaining_irr
                irr_applied[i] = applied
                current_moisture += applied
                remaining_irr -= applied

        if current_moisture >= pot_etc:
            act = pot_etc
            current_moisture -= pot_etc
        else:
            act = current_moisture
            current_moisture = 0.0

        actual_etc[i] = act

        if pot_etc > 0:
            ks = act / pot_etc
            daily_ks[i] = ks
            if ks < ks_threshold:
                is_stressed[i] = True

    return irr_applied, actual_etc, daily_ks, is_stressed


class ScenarioWaterStep(SoilWaterBucketStep):
    def apply(
        self,
        candidate: SimulationCandidate,
        context: SolverContext,
    ) -> list[SimulationCandidate]:
        curr_cand, inputs = self._prepare_inputs(candidate, context, abort_on_fatal=False)
        if inputs is None:
            return [curr_cand]

        max_irr_mm = (
            float(candidate.irrigation_application.amount_mm.real)
            if candidate.irrigation_application is not None
            else 0.0
        )

        irr_applied, actual_etc, _daily_ks, is_stressed = (
            _simulate_capped_irrigation_bucket_and_stress(
                total_days=inputs.total_days,
                taw=inputs.taw_mm,
                raw=inputs.raw_mm,
                max_irr_mm=max_irr_mm,
                potential_etc=inputs.potential_etc,
                precip=inputs.precip_slice,
                irr_eligible_mask=inputs.irr_eligible_mask,
                ks_threshold=SEVERE_STRESS_KS_THRESHOLD,
            )
        )

        variant = self._evaluate_trajectory(
            curr_cand=curr_cand,
            context=context,
            inputs=inputs,
            irr_applied=irr_applied,
            actual_etc=actual_etc,
            is_stressed=is_stressed,
            abort_on_fatal=False,
        )
        return [variant]
