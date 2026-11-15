from __future__ import annotations

from dataclasses import dataclass, replace
from typing import Final

from app.models.agronomy import Crop
from app.models.planning import SolverContext
from app.models.topology import SoilProperties
from app.models.types import (
    CropId,
    DeathNote,
    FieldId,
    Note,
    NoteCategory,
    NoteImportance,
    YieldMultiplier,
)
from app.preprocessing.candidate import SimulationCandidate
from app.preprocessing.steps.base import SimulationStep

SALINITY_MARGIN_DS_M: Final[float] = 0.30
PH_MARGIN: Final[float] = 0.25

MARGINAL_PH_YIELD_MULTIPLIER: Final[float] = 0.90
MARGINAL_SALINITY_YIELD_MULTIPLIER: Final[float] = 0.85
CRITICAL_SOIL_YIELD_THRESHOLD: Final[float] = 0.75
MIN_OPTIMIZATION_SOIL_MULTIPLIER: Final[float] = 0.50
YIELD_MULTIPLIER_ROUND_DECIMALS: Final[int] = 3


@dataclass(slots=True, frozen=True)
class _SoilRiskAction:
    note: Note
    is_fatal: bool
    death_note: DeathNote | None


@dataclass(slots=True, frozen=True)
class _SoilEvaluationResult:
    actions: tuple[_SoilRiskAction, ...]
    soil_multiplier: float


def _evaluate_soil_for_crop(
        crop: Crop,
        soil: SoilProperties,
        context: SolverContext,
        *,
        stop_on_fatal: bool = True,
) -> _SoilEvaluationResult:
    actions: list[_SoilRiskAction] = []
    combined_soil_multiplier = 1.0

    if soil.initial_depletion_mm is None:
        actions.append(
            _SoilRiskAction(
                Note(
                    NoteCategory.DATA_MISSING,
                    NoteImportance.WARN,
                    "Initial soil moisture data is missing",
                ),
                is_fatal=False,
                death_note=DeathNote("SoilCheck:MissingInitialDepletion", is_static=True),
            )
        )

    # Physical soil incompatibilities (texture & drainage) always prune during optimization
    # so unviable crops do not flood CP-SAT in risk-tolerant presets.
    texture = soil.texture
    if crop.allowed_soil_textures is not None:
        if texture not in crop.allowed_soil_textures:
            actions.append(
                _SoilRiskAction(
                    Note(
                        NoteCategory.AGRONOMIC,
                        NoteImportance.RISK,
                        f"The {texture.value} soil texture is incompatible with this crop",
                    ),
                    True,
                    DeathNote("SoilCheck:TextureNotAllowed", is_static=True),
                ),
            )
            if stop_on_fatal:
                return _SoilEvaluationResult(tuple(actions), 1.0)
    else:
        actions.append(
            _SoilRiskAction(
                Note(
                    NoteCategory.DATA_MISSING,
                    NoteImportance.WARN,
                    f"Missing texture compatibility data for {texture.value} soil",
                ),
                False,
                DeathNote("SoilCheck:MissingTextureCompatibility", is_static=True),
            ),
        )

    drainage = soil.drainage
    if crop.allowed_drainage_classes is not None:
        if drainage not in crop.allowed_drainage_classes:
            actions.append(
                _SoilRiskAction(
                    Note(
                        NoteCategory.AGRONOMIC,
                        NoteImportance.RISK,
                        f"The crop cannot grow in soil with {drainage.value} drainage",
                    ),
                    True,
                    DeathNote("SoilCheck:DrainageNotAllowed", is_static=True),
                ),
            )
            if stop_on_fatal:
                return _SoilEvaluationResult(tuple(actions), 1.0)
    else:
        actions.append(
            _SoilRiskAction(
                Note(
                    NoteCategory.DATA_MISSING,
                    NoteImportance.WARN,
                    f"Missing drainage compatibility data for {drainage.value} soil",
                ),
                False,
                DeathNote("SoilCheck:MissingDrainageCompatibility", is_static=True),
            ),
        )

    ph = soil.ph
    if crop.ph_min is not None and crop.ph_max is not None:
        if ph < crop.ph_min:
            is_fatal = not context.risk.allow_agronomic_risks
            actions.append(
                _SoilRiskAction(
                    Note(
                        NoteCategory.AGRONOMIC,
                        NoteImportance.RISK,
                        f"The soil pH of {ph} is too acidic for this crop, which needs at least {crop.ph_min}",
                    ),
                    is_fatal,
                    DeathNote("SoilCheck:PhTooLow", is_static=True),
                ),
            )
            if stop_on_fatal and is_fatal:
                return _SoilEvaluationResult(tuple(actions), 1.0)
        elif ph > crop.ph_max:
            is_fatal = not context.risk.allow_agronomic_risks
            actions.append(
                _SoilRiskAction(
                    Note(
                        NoteCategory.AGRONOMIC,
                        NoteImportance.RISK,
                        f"The soil pH of {ph} is too alkaline for this crop, which needs at most {crop.ph_max}",
                    ),
                    is_fatal,
                    DeathNote("SoilCheck:PhTooHigh", is_static=True),
                ),
            )
            if stop_on_fatal and is_fatal:
                return _SoilEvaluationResult(tuple(actions), 1.0)
        elif (ph - crop.ph_min) <= PH_MARGIN:
            actions.append(
                _SoilRiskAction(
                    Note(
                        NoteCategory.AGRONOMIC,
                        NoteImportance.WARN,
                        f"The soil pH of {ph} is close to the minimum limit of {crop.ph_min}",
                    ),
                    False,
                    DeathNote("SoilCheck:MarginalPh", is_static=True),
                ),
            )
            combined_soil_multiplier *= MARGINAL_PH_YIELD_MULTIPLIER
        elif (crop.ph_max - ph) <= PH_MARGIN:
            actions.append(
                _SoilRiskAction(
                    Note(
                        NoteCategory.AGRONOMIC,
                        NoteImportance.WARN,
                        f"The soil pH of {ph} is close to the maximum limit of {crop.ph_max}",
                    ),
                    False,
                    DeathNote("SoilCheck:MarginalPh", is_static=True),
                ),
            )
            combined_soil_multiplier *= MARGINAL_PH_YIELD_MULTIPLIER
    else:
        actions.append(
            _SoilRiskAction(
                Note(
                    NoteCategory.DATA_MISSING,
                    NoteImportance.WARN,
                    "Missing soil pH tolerance data",
                ),
                False,
                DeathNote("SoilCheck:MissingPhTolerance", is_static=True),
            ),
        )

    salinity_ds_m = soil.salinity_ds_m
    threshold = crop.salinity_threshold_ds_m
    slope_pct = crop.salinity_slope_pct

    if threshold is not None and slope_pct is not None and salinity_ds_m > threshold:
        # Apply FAO-29 Maas-Hoffman salinity yield penalty model
        loss_fraction = (slope_pct / 100.0) * (salinity_ds_m - threshold)
        yield_mult = max(0.0, 1.0 - loss_fraction)

        if yield_mult <= 0.0:
            actions.append(
                _SoilRiskAction(
                    Note(
                        NoteCategory.AGRONOMIC,
                        NoteImportance.RISK,
                        f"The soil salinity of {salinity_ds_m} dS/m is too high for the crop to survive",
                    ),
                    True,
                    DeathNote("SoilCheck:LethalSalinity", is_static=True),
                ),
            )
            return _SoilEvaluationResult(tuple(actions), 0.0)
        if yield_mult < 1.0:
            is_critical = yield_mult <= CRITICAL_SOIL_YIELD_THRESHOLD
            penalty_pct = round(loss_fraction * 100.0, 1)
            actions.append(
                _SoilRiskAction(
                    Note(
                        NoteCategory.AGRONOMIC,
                        NoteImportance.RISK,
                        f"High soil salinity of {salinity_ds_m} dS/m reduced the yield by {penalty_pct}%",
                    ),
                    is_critical and not context.risk.allow_agronomic_risks,
                    DeathNote("SoilCheck:CriticalSalinity", is_static=True),
                    ),
            )
            if crop.max_salinity_ds_m and salinity_ds_m > crop.max_salinity_ds_m:
                is_fatal = not context.risk.allow_agronomic_risks
                actions.append(
                    _SoilRiskAction(
                        Note(
                            NoteCategory.AGRONOMIC,
                            NoteImportance.RISK,
                            f"The soil salinity of {salinity_ds_m} dS/m exceeds the crop's maximum limit of {crop.max_salinity_ds_m} dS/m",
                        ),
                        is_fatal,
                        DeathNote("SoilCheck:SalinityTooHigh", is_static=True),
                    ),
                )
            combined_soil_multiplier *= yield_mult
            return _SoilEvaluationResult(tuple(actions), combined_soil_multiplier)

    if crop.max_salinity_ds_m is not None:
        if salinity_ds_m > crop.max_salinity_ds_m:
            is_fatal = not context.risk.allow_agronomic_risks
            actions.append(
                _SoilRiskAction(
                    Note(
                        NoteCategory.AGRONOMIC,
                        NoteImportance.RISK,
                        f"The soil salinity of {salinity_ds_m} dS/m exceeds the crop's tolerance of {crop.max_salinity_ds_m} dS/m",
                    ),
                    is_fatal,
                    DeathNote("SoilCheck:SalinityTooHigh", is_static=True),
                ),
            )
            if stop_on_fatal and is_fatal:
                return _SoilEvaluationResult(tuple(actions), 1.0)
        elif (crop.max_salinity_ds_m - salinity_ds_m) <= SALINITY_MARGIN_DS_M:
            actions.append(
                _SoilRiskAction(
                    Note(
                        NoteCategory.AGRONOMIC,
                        NoteImportance.WARN,
                        f"The soil salinity of {salinity_ds_m} dS/m is close to the crop's maximum tolerance",
                    ),
                    False,
                    DeathNote("SoilCheck:MarginalSalinity", is_static=True),
                ),
            )
            combined_soil_multiplier *= MARGINAL_SALINITY_YIELD_MULTIPLIER
    else:
        actions.append(
            _SoilRiskAction(
                Note(
                    NoteCategory.DATA_MISSING,
                    NoteImportance.WARN,
                    "Missing soil salinity tolerance data",
                ),
                False,
                DeathNote("SoilCheck:MissingSalinityTolerance", is_static=True),
            ),
        )

    return _SoilEvaluationResult(tuple(actions), combined_soil_multiplier)


class SoilConstraintStep(SimulationStep):
    def __init__(self) -> None:
        self._cache: dict[tuple[FieldId, CropId, bool], _SoilEvaluationResult] = {}

    def apply(
            self,
            candidate: SimulationCandidate,
            context: SolverContext,
    ) -> list[SimulationCandidate]:
        if candidate.is_dead:
            return [candidate]

        f_id = candidate.field.id
        c_id = candidate.profile.crop.id
        cache_key = (f_id, c_id, context.risk.allow_agronomic_risks)

        eval_res = self._cache.get(cache_key)
        if eval_res is None:
            eval_res = _evaluate_soil_for_crop(
                crop=candidate.profile.crop,
                soil=candidate.field.soil,
                context=context,
            )
            self._cache[cache_key] = eval_res

        current_candidate = candidate

        if eval_res.actions:
            for act in eval_res.actions:
                current_candidate = current_candidate.with_risk(
                    note=act.note,
                    fatal_if=act.is_fatal,
                    death_note=act.death_note,
                )
                if current_candidate.is_dead:
                    return [current_candidate]

        if eval_res.soil_multiplier < MIN_OPTIMIZATION_SOIL_MULTIPLIER:
            return [
                current_candidate.killed(
                    DeathNote("SoilCheck:SevereSoilPenalty", is_static=True),
                ),
            ]

        if eval_res.soil_multiplier < 1.0:
            new_mults: tuple[YieldMultiplier, ...]
            if not current_candidate.yield_multipliers:
                new_mults = (
                    YieldMultiplier(
                        round(eval_res.soil_multiplier, YIELD_MULTIPLIER_ROUND_DECIMALS),
                    ),
                )
            else:
                new_mults = tuple(
                    YieldMultiplier(
                        round(
                            float(m.real) * eval_res.soil_multiplier,
                            YIELD_MULTIPLIER_ROUND_DECIMALS,
                            ),
                    )
                    for m in current_candidate.yield_multipliers
                )
            current_candidate = replace(current_candidate, yield_multipliers=new_mults)

        return [current_candidate]


class ScenarioSoilConstraintStep(SoilConstraintStep):
    def apply(
            self,
            candidate: SimulationCandidate,
            context: SolverContext,
    ) -> list[SimulationCandidate]:
        cache_key = (
            candidate.field.id,
            candidate.profile.crop.id,
            context.risk.allow_agronomic_risks,
        )
        eval_res = self._cache.get(cache_key)
        if eval_res is None:
            eval_res = _evaluate_soil_for_crop(
                crop=candidate.profile.crop,
                soil=candidate.field.soil,
                context=context,
                stop_on_fatal=False,
            )
            self._cache[cache_key] = eval_res

        current_candidate = candidate
        for act in eval_res.actions:
            current_candidate = current_candidate.with_risk(note=act.note)

        if eval_res.soil_multiplier < 1.0:
            if not current_candidate.yield_multipliers:
                new_mults: tuple[YieldMultiplier, ...] = (
                    YieldMultiplier(
                        round(eval_res.soil_multiplier, YIELD_MULTIPLIER_ROUND_DECIMALS),
                    ),
                )
            else:
                new_mults = tuple(
                    YieldMultiplier(
                        round(
                            float(m.real) * eval_res.soil_multiplier,
                            YIELD_MULTIPLIER_ROUND_DECIMALS,
                            ),
                    )
                    for m in current_candidate.yield_multipliers
                )
            current_candidate = replace(current_candidate, yield_multipliers=new_mults)

        return [current_candidate]