from __future__ import annotations

from dataclasses import dataclass, replace
from datetime import date, timedelta
from typing import Any, Final

import numpy as np

from app.models.agronomy import CropProfile, CropProfileStage
from app.models.planning import SolverContext
from app.models.types import (
    DayOffset,
    DeathNote,
    Note,
    NoteCategory,
    NoteImportance,
    StageCategory,
)
from app.preprocessing.candidate import SimulationCandidate
from app.preprocessing.steps.base import SimulationStep

MONTHS_IN_YEAR: Final[int] = 12
HORIZON_START_DAY: Final[int] = 1
FEBRUARY_LEAP_FALLBACK_DAY: Final[int] = 28
DEFAULT_MIN_DAYS: Final[int] = 0
INITIAL_OFFSET_DAYS: Final[int] = 0

ZERO_GDD: Final[float] = 0.0
ZERO_VD: Final[float] = 0.0
VD_RESPONSE_ZERO: Final[float] = 0.0
VD_RESPONSE_FULL: Final[float] = 1.0
DEVERNALIZATION_MIN_PENALTY: Final[float] = 0.0
DEVERNALIZATION_MAX_PENALTY: Final[float] = -1.0

STAGE_TIMEOUT_LETHAL_FACTOR: Final[float] = 1.25


def _calc_gdd_slice(
    daily_means: np.ndarray,
    base_t: float,
    opt_min: float | None,
    opt_max: float | None,
    abs_max: float | None,
) -> np.ndarray:
    diff = daily_means - base_t
    if opt_min is not None and opt_max is not None:
        if opt_min <= base_t:
            return np.clip(diff, ZERO_GDD, opt_max - base_t)

        daily_gdd = np.maximum(ZERO_GDD, diff)
        max_gdd = opt_min - base_t
        if abs_max is not None and abs_max > opt_max:
            gdd_decrease = (
                max_gdd * np.maximum(ZERO_GDD, abs_max - daily_means) / (abs_max - opt_max)
            )
            np.minimum(daily_gdd, gdd_decrease, out=daily_gdd)
        np.minimum(daily_gdd, max_gdd, out=daily_gdd)
        return daily_gdd

    return np.maximum(ZERO_GDD, diff)


@dataclass(slots=True)
class _StageCache:
    target_gdd: float
    target_vd: float
    min_days: int
    max_days: int | None
    category: StageCategory
    label: str
    gdd_end_indices: np.ndarray | None
    vd_end_indices: np.ndarray | None
    risk_note_gdd: Note
    death_note_gdd: DeathNote
    risk_note_vd: Note
    death_note_vd: DeathNote


@dataclass(slots=True)
class _ProfileCache:
    fatal_death_note: DeathNote | None
    fatal_risk_note: Note | None
    stages: list[_StageCache]
    is_perennial: bool
    restart_idx: int | None
    max_lifespan_years: int | None
    death_note_timeout: dict[str, DeathNote]
    death_note_exceed_climate: DeathNote
    risk_note_exceed_climate: Note


def _validate_stage_params(
    stage: CropProfileStage,
    context: SolverContext,
) -> tuple[DeathNote | None, Note | None]:
    target_gdd = (
        float(stage.target_accumulated_gdd.real)
        if stage.target_accumulated_gdd is not None
        else ZERO_GDD
    )
    if (
        target_gdd > ZERO_GDD
        and stage.gdd_base_temp_c is None
        and not context.risk.allow_missing_data
    ):
        return (
            DeathNote(f"Phenology:MissingGddParams({stage.label})", is_static=True),
            Note(
                NoteCategory.DATA_MISSING,
                NoteImportance.RISK,
                f"Missing GDD base temperature: {stage.label}",
            ),
        )

    target_vd = (
        float(stage.target_accumulated_vd.real)
        if stage.target_accumulated_vd is not None
        else ZERO_VD
    )
    if target_vd > ZERO_VD:
        if (
            stage.vernalization_optimal_min_temp_c is None
            or stage.vernalization_optimal_max_temp_c is None
        ):
            if not context.risk.allow_missing_data:
                return (
                    DeathNote(f"Phenology:MissingVdParams({stage.label})", is_static=True),
                    Note(
                        NoteCategory.DATA_MISSING,
                        NoteImportance.RISK,
                        f"Missing vernalization range: {stage.label}",
                    ),
                )
        else:
            on = float(stage.vernalization_optimal_min_temp_c.real)
            ox = float(stage.vernalization_optimal_max_temp_c.real)
            if on > ox and not context.risk.allow_missing_data:
                return (
                    DeathNote(f"Phenology:InvalidVdParams({stage.label})", is_static=True),
                    Note(
                        NoteCategory.DATA_MISSING,
                        NoteImportance.RISK,
                        f"Invalid vernalization range ({on} C > {ox} C): {stage.label}",
                    ),
                )

            an = (
                float(stage.vernalization_absolute_min_temp_c.real)
                if stage.vernalization_absolute_min_temp_c is not None
                else on
            )
            ax = (
                float(stage.vernalization_absolute_max_temp_c.real)
                if stage.vernalization_absolute_max_temp_c is not None
                else ox
            )
            if (
                (an is not None and an > on) or (ax is not None and ax < ox)
            ) and not context.risk.allow_missing_data:
                return (
                    DeathNote(f"Phenology:InvalidVdParams({stage.label})", is_static=True),
                    Note(
                        NoteCategory.DATA_MISSING,
                        NoteImportance.RISK,
                        f"Invalid vernalization thresholds: {stage.label}",
                    ),
                )

    return None, None


class PhenologyStep(SimulationStep):
    def __init__(self) -> None:
        self._climate_initialized: bool = False
        self._horizon_start: date | None = None
        self._full_means: np.ndarray = np.empty(0, dtype=np.float32)
        self._full_maxs: np.ndarray = np.empty(0, dtype=np.float32)
        self._profile_cache: dict[Any, _ProfileCache] = {}

    def _init_climate(self, context: SolverContext) -> None:
        field = context.field
        self._horizon_start = date(
            context.horizon.start.year, context.horizon.start.month, HORIZON_START_DAY
        )
        horizon_end = date(
            context.horizon.end.year,
            context.horizon.end.month,
            context.horizon.end.days_in_month,
        )
        total_days = (horizon_end - self._horizon_start).days + 1

        self._full_means = np.empty(total_days, dtype=np.float32)
        self._full_maxs = np.empty(total_days, dtype=np.float32)
        records = field.climate.records

        for i in range(total_days):
            current_date = self._horizon_start + timedelta(days=i)
            rec = records.get(current_date)
            if rec is not None:
                self._full_means[i] = float(rec.temp_mean_c.real)
                self._full_maxs[i] = float(rec.temp_max_c.real)
            else:
                self._full_means[i] = ZERO_GDD
                self._full_maxs[i] = ZERO_GDD

        self._climate_initialized = True

    def _build_profile_cache(self, profile: CropProfile, context: SolverContext) -> _ProfileCache:
        stages = profile.stages
        total_days = len(self._full_means)
        max_lifespan = (
            int(profile.max_lifespan_years) if profile.max_lifespan_years is not None else None
        )

        if not stages:
            return _ProfileCache(
                fatal_death_note=DeathNote("Phenology:NoStagesDefined", is_static=True),
                fatal_risk_note=Note(
                    NoteCategory.DATA_MISSING,
                    NoteImportance.RISK,
                    "No phenological stages defined",
                ),
                stages=[],
                is_perennial=profile.is_perennial,
                restart_idx=None,
                max_lifespan_years=max_lifespan,
                death_note_timeout={},
                death_note_exceed_climate=DeathNote(
                    "Phenology:TotalDaysExceedClimate",
                    is_static=False,
                ),
                risk_note_exceed_climate=Note(
                    NoteCategory.AGRONOMIC,
                    NoteImportance.RISK,
                    "Crop cycle exceeded climate horizon",
                ),
            )

        for stage in stages:
            death_note, risk_note = _validate_stage_params(stage, context)
            if death_note is not None:
                return _ProfileCache(
                    fatal_death_note=death_note,
                    fatal_risk_note=risk_note,
                    stages=[],
                    is_perennial=profile.is_perennial,
                    restart_idx=None,
                    max_lifespan_years=max_lifespan,
                    death_note_timeout={},
                    death_note_exceed_climate=DeathNote(
                        "Phenology:TotalDaysExceedClimate",
                        is_static=False,
                    ),
                    risk_note_exceed_climate=Note(
                        NoteCategory.AGRONOMIC,
                        NoteImportance.RISK,
                        "Crop cycle exceeded climate horizon",
                    ),
                )

        stage_caches: list[_StageCache] = []
        timeout_notes: dict[str, DeathNote] = {}

        bases_template = np.empty(total_days, dtype=np.float32)
        bases_template[0] = ZERO_GDD

        for stage in stages:
            label = stage.label
            timeout_notes[label] = DeathNote(f"Phenology:StageTimeout({label})", is_static=False)

            target_gdd = (
                float(stage.target_accumulated_gdd.real)
                if stage.target_accumulated_gdd is not None
                else ZERO_GDD
            )
            gdd_end_indices = None

            if target_gdd > ZERO_GDD and stage.gdd_base_temp_c is not None:
                base_t = float(stage.gdd_base_temp_c.real)
                opt_min = (
                    float(stage.gdd_optimum_min_temp_c.real)
                    if stage.gdd_optimum_min_temp_c is not None
                    else None
                )
                opt_max = (
                    float(stage.gdd_optimum_max_temp_c.real)
                    if stage.gdd_optimum_max_temp_c is not None
                    else None
                )
                abs_max = (
                    float(stage.absolute_max_temp_c.real)
                    if stage.absolute_max_temp_c is not None
                    else None
                )
                daily_gdd = _calc_gdd_slice(self._full_means, base_t, opt_min, opt_max, abs_max)
                cum_gdd = np.cumsum(daily_gdd, dtype=np.float32)

                bases = bases_template.copy()
                bases[1:] = cum_gdd[:-1]
                targets = target_gdd + bases
                gdd_end_indices = np.searchsorted(cum_gdd, targets).astype(np.int32)

            target_vd = (
                float(stage.target_accumulated_vd.real)
                if stage.target_accumulated_vd is not None
                else ZERO_VD
            )
            vd_end_indices = None

            if (
                target_vd > ZERO_VD
                and stage.vernalization_optimal_min_temp_c is not None
                and stage.vernalization_optimal_max_temp_c is not None
            ):
                on = float(stage.vernalization_optimal_min_temp_c.real)
                ox = float(stage.vernalization_optimal_max_temp_c.real)
                an = (
                    float(stage.vernalization_absolute_min_temp_c.real)
                    if stage.vernalization_absolute_min_temp_c is not None
                    else on
                )
                ax = (
                    float(stage.vernalization_absolute_max_temp_c.real)
                    if stage.vernalization_absolute_max_temp_c is not None
                    else ox
                )
                xp = [min(an, on), on, ox, max(ax, ox)]
                daily_vd = np.interp(
                    self._full_means,
                    xp,
                    [VD_RESPONSE_ZERO, VD_RESPONSE_FULL, VD_RESPONSE_FULL, VD_RESPONSE_ZERO],
                ).astype(np.float32)

                dev_start_c = stage.devernalization_start_temp_c
                dev_full_c = stage.devernalization_full_temp_c

                if (
                    dev_start_c is not None
                    and dev_full_c is not None
                    and float(dev_full_c.real) > float(dev_start_c.real)
                ):
                    dev_start = float(dev_start_c.real)
                    dev_full = float(dev_full_c.real)
                    daily_devernalization = np.interp(
                        self._full_maxs,
                        [dev_start, dev_full],
                        [DEVERNALIZATION_MIN_PENALTY, DEVERNALIZATION_MAX_PENALTY],
                    ).astype(np.float32)
                    daily_vd += daily_devernalization

                if np.any(daily_vd < ZERO_VD):
                    vd_ends = np.full(total_days, total_days, dtype=np.int32)
                    for start_d in range(total_days):
                        acc = ZERO_VD
                        for d in range(start_d, total_days):
                            val = daily_vd[d]
                            acc = max(ZERO_VD, acc + val)
                            if acc >= target_vd:
                                vd_ends[start_d] = d
                                break
                    vd_end_indices = vd_ends
                else:
                    cum_vd = np.cumsum(daily_vd, dtype=np.float32)
                    bases_vd = bases_template.copy()
                    bases_vd[1:] = cum_vd[:-1]
                    targets_vd = target_vd + bases_vd
                    vd_end_indices = np.searchsorted(cum_vd, targets_vd).astype(np.int32)

            stage_caches.append(
                _StageCache(
                    target_gdd=target_gdd,
                    target_vd=target_vd,
                    min_days=int(stage.min_days_since_planting or DEFAULT_MIN_DAYS),
                    max_days=int(stage.max_days_since_planting)
                    if stage.max_days_since_planting
                    else None,
                    category=stage.category,
                    label=label,
                    gdd_end_indices=gdd_end_indices,
                    vd_end_indices=vd_end_indices,
                    risk_note_gdd=Note(
                        NoteCategory.AGRONOMIC,
                        NoteImportance.RISK,
                        f"Insufficient GDD: {label}",
                    ),
                    death_note_gdd=DeathNote(f"Phenology:UnfinishedGDD({label})", is_static=False),
                    risk_note_vd=Note(
                        NoteCategory.AGRONOMIC,
                        NoteImportance.RISK,
                        f"Insufficient vernalization: {label}",
                    ),
                    death_note_vd=DeathNote(f"Phenology:UnfinishedVD({label})", is_static=False),
                ),
            )

        restart_idx = None
        if profile.is_perennial and profile.perennial_restart_stage is not None:
            for idx, s in enumerate(stages):
                if s.sequence_order == profile.perennial_restart_stage:
                    restart_idx = idx
                    break

        return _ProfileCache(
            fatal_death_note=None,
            fatal_risk_note=None,
            stages=stage_caches,
            is_perennial=profile.is_perennial,
            restart_idx=restart_idx,
            max_lifespan_years=max_lifespan,
            death_note_timeout=timeout_notes,
            death_note_exceed_climate=DeathNote(
                "Phenology:TotalDaysExceedClimate",
                is_static=False,
            ),
            risk_note_exceed_climate=Note(
                NoteCategory.AGRONOMIC,
                NoteImportance.RISK,
                "Crop cycle exceeded climate horizon",
            ),
        )

    def apply(
        self,
        candidate: SimulationCandidate,
        context: SolverContext,
    ) -> list[SimulationCandidate]:
        if candidate.is_dead:
            return [candidate]

        if not self._climate_initialized:
            self._init_climate(context)

        assert self._horizon_start is not None

        prof_id = candidate.profile.id
        pc = self._profile_cache.get(prof_id)
        if pc is None:
            pc = self._build_profile_cache(candidate.profile, context)
            self._profile_cache[prof_id] = pc

        if pc.fatal_death_note is not None and pc.fatal_risk_note is not None:
            return [
                candidate.with_risk(
                    note=pc.fatal_risk_note,
                    fatal_if=True,
                    death_note=pc.fatal_death_note,
                ),
            ]

        plant_idx = (candidate.plant_date - self._horizon_start).days
        total_horizon_len = len(self._full_means)
        max_duration_days = total_horizon_len - plant_idx

        if max_duration_days <= 0:
            return [
                candidate.with_risk(
                    note=Note(
                        NoteCategory.DATA_MISSING,
                        NoteImportance.RISK,
                        "Missing climate weather series",
                    ),
                    fatal_if=True,
                    death_note=DeathNote("Phenology:EmptyClimateArray", is_static=True),
                ),
            ]

        if pc.is_perennial and pc.max_lifespan_years is not None:
            p_date = candidate.plant_date
            try:
                end_date = date(p_date.year + pc.max_lifespan_years, p_date.month, p_date.day)
            except ValueError:
                end_date = date(
                    p_date.year + pc.max_lifespan_years, p_date.month, FEBRUARY_LEAP_FALLBACK_DAY
                )
            lifespan_days = (end_date - p_date).days
            max_duration_days = min(max_duration_days, lifespan_days)

        current_offset = INITIAL_OFFSET_DAYS
        cycle_start_offset = INITIAL_OFFSET_DAYS
        stage_offsets: list[DayOffset] = []
        harvest_events: list[date] = []

        stages = pc.stages
        num_stages = len(stages)
        stage_idx = 0
        current_candidate = candidate

        while stage_idx < num_stages and current_offset < max_duration_days:
            sc = stages[stage_idx]
            abs_start = plant_idx + current_offset
            max_end_abs = plant_idx + max_duration_days

            if abs_start >= total_horizon_len:
                break

            days_gdd = 0
            if sc.gdd_end_indices is not None:
                idx = int(sc.gdd_end_indices[abs_start])
                if idx >= max_end_abs or idx >= total_horizon_len:
                    return [
                        current_candidate.with_risk(
                            note=sc.risk_note_gdd,
                            fatal_if=True,
                            death_note=sc.death_note_gdd,
                        ),
                    ]
                days_gdd = idx - abs_start + 1

            days_vd = 0
            if sc.vd_end_indices is not None:
                idx = int(sc.vd_end_indices[abs_start])
                if idx >= max_end_abs or idx >= total_horizon_len:
                    return [
                        current_candidate.with_risk(
                            note=sc.risk_note_vd,
                            fatal_if=True,
                            death_note=sc.death_note_vd,
                        ),
                    ]
                days_vd = idx - abs_start + 1

            stage_duration = max(days_vd, days_gdd)
            days_in_cycle = current_offset - cycle_start_offset
            projected_cycle_days = days_in_cycle + stage_duration

            if sc.max_days is not None and 0 < sc.max_days < projected_cycle_days:
                is_severe_timeout = projected_cycle_days > int(
                    sc.max_days * STAGE_TIMEOUT_LETHAL_FACTOR
                )
                is_fatal = is_severe_timeout or not context.risk.allow_agronomic_risks

                current_candidate = current_candidate.with_risk(
                    note=Note(
                        NoteCategory.AGRONOMIC,
                        NoteImportance.RISK,
                        f"Stage timeout: {sc.label} ({projected_cycle_days}d > max {sc.max_days}d)",
                    ),
                    fatal_if=is_fatal,
                    death_note=pc.death_note_timeout[sc.label],
                )
                if current_candidate.is_dead:
                    return [current_candidate]
            elif sc.min_days > DEFAULT_MIN_DAYS and projected_cycle_days < sc.min_days:
                stage_duration = sc.min_days - days_in_cycle

            current_offset += stage_duration
            stage_offsets.append(DayOffset(current_offset))

            if sc.category == StageCategory.HARVEST:
                harvest_events.append(current_candidate.plant_date + timedelta(days=current_offset))

            stage_idx += 1

            if stage_idx == num_stages and pc.is_perennial and pc.restart_idx is not None:
                stage_idx = pc.restart_idx
                cycle_start_offset = current_offset

        if not pc.is_perennial and stage_idx < num_stages:
            return [
                current_candidate.with_risk(
                    note=pc.risk_note_exceed_climate,
                    fatal_if=True,
                    death_note=pc.death_note_exceed_climate,
                ),
            ]

        if pc.is_perennial:
            if not harvest_events:
                return [
                    current_candidate.with_risk(
                        note=Note(
                            NoteCategory.AGRONOMIC,
                            NoteImportance.RISK,
                            "Initial harvest not reached within climate horizon",
                        ),
                        fatal_if=True,
                        death_note=DeathNote("Phenology:PerennialNoHarvest", is_static=False),
                    ),
                ]
            current_offset = min(current_offset, max_duration_days)

        if not stage_offsets:
            return [
                current_candidate.with_risk(
                    note=Note(
                        NoteCategory.AGRONOMIC,
                        NoteImportance.RISK,
                        "No phenological stages completed",
                    ),
                    fatal_if=True,
                    death_note=DeathNote("Phenology:NoStagesCompleted", is_static=True),
                ),
            ]

        final_candidate = replace(
            current_candidate,
            stage_offsets=tuple(stage_offsets),
            harvest_events=tuple(harvest_events),
            clearance_date=current_candidate.plant_date + timedelta(days=current_offset),
        )
        return [final_candidate]


class ScenarioPhenologyStep(PhenologyStep):
    def apply(
        self,
        candidate: SimulationCandidate,
        context: SolverContext,
    ) -> list[SimulationCandidate]:
        if not self._climate_initialized:
            self._init_climate(context)

        assert self._horizon_start is not None

        prof_id = candidate.profile.id
        pc = self._profile_cache.get(prof_id)
        if pc is None:
            pc = self._build_profile_cache(candidate.profile, context)
            self._profile_cache[prof_id] = pc

        if pc.fatal_death_note is not None and pc.fatal_risk_note is not None:
            return [
                candidate.with_risk(
                    note=pc.fatal_risk_note,
                    fatal_if=False,
                )
            ]

        plant_idx = (candidate.plant_date - self._horizon_start).days
        total_horizon_len = len(self._full_means)
        horizon_duration_days = total_horizon_len - plant_idx

        if plant_idx < 0 or horizon_duration_days <= 0:
            return [
                candidate.with_risk(
                    note=Note(
                        NoteCategory.AGRONOMIC,
                        NoteImportance.RISK,
                        f"Plant date ({candidate.plant_date}) is outside planning horizon "
                        f"({context.horizon.start} to {context.horizon.end})",
                    ),
                    fatal_if=False,
                ),
            ]

        max_duration_days = horizon_duration_days
        current_candidate = candidate

        user_clearance = candidate.clearance_date
        user_days: int | None = None
        if user_clearance is not None:
            user_days = (user_clearance - candidate.plant_date).days
            if user_days <= 0:
                return [
                    current_candidate.with_risk(
                        note=Note(
                            NoteCategory.AGRONOMIC,
                            NoteImportance.RISK,
                            f"Clearance date ({user_clearance}) must be after plant date ({candidate.plant_date})",
                        ),
                        fatal_if=False,
                    ),
                ]
            if user_days > horizon_duration_days:
                current_candidate = current_candidate.with_risk(
                    note=Note(
                        NoteCategory.AGRONOMIC,
                        NoteImportance.RISK,
                        f"Clearance date ({user_clearance}) exceeds planning horizon "
                        f"({context.horizon.start} to {context.horizon.end})",
                    ),
                    fatal_if=False,
                )
            max_duration_days = min(max_duration_days, user_days)

        if pc.is_perennial and pc.max_lifespan_years is not None:
            p_date = candidate.plant_date
            try:
                end_date = date(p_date.year + pc.max_lifespan_years, p_date.month, p_date.day)
            except ValueError:
                end_date = date(
                    p_date.year + pc.max_lifespan_years, p_date.month, FEBRUARY_LEAP_FALLBACK_DAY
                )
            lifespan_days = (end_date - p_date).days
            if user_days is not None and user_days > lifespan_days:
                current_candidate = current_candidate.with_risk(
                    note=Note(
                        NoteCategory.AGRONOMIC,
                        NoteImportance.WARN,
                        f"Clearance date ({user_clearance}) exceeds crop max lifespan of "
                        f"{pc.max_lifespan_years} year(s) (ends {end_date}); "
                        f"harvests stop at {end_date} while field remains occupied",
                    ),
                    fatal_if=False,
                )
            max_duration_days = min(max_duration_days, lifespan_days)

        capped_by_user = user_days is not None and user_days <= horizon_duration_days

        current_offset = INITIAL_OFFSET_DAYS
        cycle_start_offset = INITIAL_OFFSET_DAYS
        stage_offsets: list[DayOffset] = []
        harvest_events: list[date] = []

        stages = pc.stages
        num_stages = len(stages)
        stage_idx = 0
        truncated = False

        while stage_idx < num_stages and current_offset < max_duration_days:
            sc = stages[stage_idx]
            abs_start = plant_idx + current_offset
            max_end_abs = plant_idx + max_duration_days

            if abs_start >= total_horizon_len:
                break

            days_gdd = 0
            if sc.gdd_end_indices is not None:
                idx = int(sc.gdd_end_indices[abs_start])
                if idx >= max_end_abs or idx >= total_horizon_len:
                    if not capped_by_user:
                        current_candidate = current_candidate.with_risk(
                            note=sc.risk_note_gdd, fatal_if=False
                        )
                    current_offset = max_duration_days
                    stage_offsets.append(DayOffset(current_offset))
                    truncated = True
                    break
                days_gdd = idx - abs_start + 1

            days_vd = 0
            if sc.vd_end_indices is not None:
                idx = int(sc.vd_end_indices[abs_start])
                if idx >= max_end_abs or idx >= total_horizon_len:
                    if not capped_by_user:
                        current_candidate = current_candidate.with_risk(
                            note=sc.risk_note_vd, fatal_if=False
                        )
                    current_offset = max_duration_days
                    stage_offsets.append(DayOffset(current_offset))
                    truncated = True
                    break
                days_vd = idx - abs_start + 1

            stage_duration = max(days_vd, days_gdd)
            days_in_cycle = current_offset - cycle_start_offset
            projected_cycle_days = days_in_cycle + stage_duration

            if sc.max_days is not None and 0 < sc.max_days < projected_cycle_days:
                current_candidate = current_candidate.with_risk(
                    note=Note(
                        NoteCategory.AGRONOMIC,
                        NoteImportance.RISK,
                        f"Stage timeout: {sc.label} ({projected_cycle_days}d > max {sc.max_days}d)",
                    ),
                    fatal_if=False,
                )
            elif sc.min_days > DEFAULT_MIN_DAYS and projected_cycle_days < sc.min_days:
                stage_duration = sc.min_days - days_in_cycle

            if current_offset + stage_duration > max_duration_days:
                if not capped_by_user:
                    current_candidate = current_candidate.with_risk(
                        note=pc.risk_note_exceed_climate, fatal_if=False
                    )
                current_offset = max_duration_days
                stage_offsets.append(DayOffset(current_offset))
                truncated = True
                break

            current_offset += stage_duration
            stage_offsets.append(DayOffset(current_offset))

            if sc.category == StageCategory.HARVEST:
                harvest_events.append(current_candidate.plant_date + timedelta(days=current_offset))

            stage_idx += 1

            if stage_idx == num_stages and pc.is_perennial and pc.restart_idx is not None:
                stage_idx = pc.restart_idx
                cycle_start_offset = current_offset

        if capped_by_user and (
            not pc.is_perennial and stage_idx < num_stages or not harvest_events
        ):
            current_candidate = current_candidate.with_risk(
                note=Note(
                    NoteCategory.AGRONOMIC,
                    NoteImportance.RISK,
                    f"Crop cycle truncated by clearance date ({user_clearance}) before maturity",
                ),
                fatal_if=False,
            )
        elif not capped_by_user:
            if not truncated and not pc.is_perennial and stage_idx < num_stages:
                current_candidate = current_candidate.with_risk(
                    note=pc.risk_note_exceed_climate, fatal_if=False
                )
            if pc.is_perennial and not harvest_events:
                current_candidate = current_candidate.with_risk(
                    note=Note(
                        NoteCategory.AGRONOMIC,
                        NoteImportance.RISK,
                        "Initial harvest not reached within climate horizon",
                    ),
                    fatal_if=False,
                )

        if pc.is_perennial:
            current_offset = min(current_offset, max_duration_days)

        if not stage_offsets:
            return [
                current_candidate.with_risk(
                    note=Note(
                        NoteCategory.AGRONOMIC,
                        NoteImportance.RISK,
                        "No phenological stages completed",
                    ),
                    fatal_if=False,
                ),
            ]

        final_clearance = (
            user_clearance
            if user_clearance is not None
            else current_candidate.plant_date + timedelta(days=current_offset)
        )
        final_candidate = replace(
            current_candidate,
            stage_offsets=tuple(stage_offsets),
            harvest_events=tuple(harvest_events),
            clearance_date=final_clearance,
        )
        return [final_candidate]
