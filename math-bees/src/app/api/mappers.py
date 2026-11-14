from __future__ import annotations

from typing import Any

from app.autogen.api.schemas import (
    CropProfileCycle,
    HarvestYearMonth,
    OptimizationResultStatus,
    OptimizeFieldRequest,
    OptimizeFieldResponse,
)
from app.models.planning import Horizon
from app.models.types import FieldId, MaterialId
from app.models.year_month import YearMonth
from app.solver.result import SolverResult
from app.solver.settings import FertilizerSetting, IrrigationSetting, RiskSetting
from app.solver.strategy import (
    ReplenishmentSetting,
    SolverStrategy,
)


def _parse_year_month(ym_str: str) -> YearMonth:
    return YearMonth.from_str(ym_str)


_RESOLUTION_LEVELS: dict[str, tuple[float, ...]] = {
    "BINARY": (0.0, 1.0),
    "TERNARY": (0.0, 0.5, 1.0),
    "QUATERNARY": (0.0, 0.5, 0.8, 1.0),
    "CONTINUOUS": (0.0, 0.2, 0.4, 0.6, 0.8, 1.0),
}


def _resolve_levels(resolution_enum: Any | None) -> tuple[float, ...]:
    if not resolution_enum:
        return (0.0,)

    resolution_val = getattr(resolution_enum, "value", str(resolution_enum))

    return _RESOLUTION_LEVELS.get(resolution_val, (0.0,))


def map_request_to_domain(
    request: OptimizeFieldRequest,
) -> tuple[FieldId, Horizon, SolverStrategy]:
    field_id = FieldId(request.field_id)
    horizon = Horizon(
        start=_parse_year_month(request.horizon.start_year_month),
        end=_parse_year_month(request.horizon.end_year_month),
    )
    strategy_settings = getattr(request, "strategy", None)

    risk_dto = strategy_settings.risk if strategy_settings else None

    allow_missing: bool = True
    allow_agronomic: bool = True

    if risk_dto:
        if getattr(risk_dto, "allow_missing_data", None) is not None:
            allow_missing = bool(risk_dto.allow_missing_data)
        if getattr(risk_dto, "allow_agronomic_risks", None) is not None:
            allow_agronomic = bool(risk_dto.allow_agronomic_risks)

    risk = RiskSetting(
        allow_missing_data=allow_missing,
        allow_agronomic_risks=allow_agronomic,
    )

    repl_dto = strategy_settings.replenishment if strategy_settings else None
    irr_dto = getattr(repl_dto, "irrigation", None) if repl_dto else None
    fert_dto = getattr(repl_dto, "fertilizers", None) if repl_dto else None

    irr_apply = bool(getattr(irr_dto, "apply", False))
    irr_mat_id = (
        MaterialId(str(irr_dto.material_id))
        if irr_dto and getattr(irr_dto, "material_id", None) is not None
        else None
    )

    if irr_apply:
        raw_resolution = getattr(irr_dto, "resolution", None)
        irr_levels = _resolve_levels(raw_resolution)
    else:
        irr_levels = (0.0,)

    irrigation = IrrigationSetting(
        apply=irr_apply,
        material_id=irr_mat_id,
        levels=irr_levels,
    )

    fert_apply = bool(getattr(fert_dto, "apply", False))

    if fert_apply:
        raw_resolution = getattr(fert_dto, "resolution", None)
        fert_levels = _resolve_levels(raw_resolution)
    else:
        fert_levels = (0.0,)

    fertilizers = FertilizerSetting(
        apply=fert_apply,
        n_material_id=(
            MaterialId(str(fert_dto.n_material_id))
            if fert_dto and getattr(fert_dto, "n_material_id", None) is not None
            else None
        ),
        p_material_id=(
            MaterialId(str(fert_dto.p_material_id))
            if fert_dto and getattr(fert_dto, "p_material_id", None) is not None
            else None
        ),
        k_material_id=(
            MaterialId(str(fert_dto.k_material_id))
            if fert_dto and getattr(fert_dto, "k_material_id", None) is not None
            else None
        ),
        levels=fert_levels,
    )

    replenishment = ReplenishmentSetting(
        irrigation=irrigation,
        fertilizers=fertilizers,
    )

    strategy = SolverStrategy(
        risk=risk,
        replenishment=replenishment,
    )

    return field_id, horizon, strategy


def map_result_to_response(result: SolverResult) -> OptimizeFieldResponse:
    cycles: list[CropProfileCycle] = []

    total_profit_cents = int(result.expected_revenue) if result.expected_revenue else 0

    if result.is_feasible:
        for v in result.schedule.chronological():
            cycle = CropProfileCycle(
                crop_profile_id=str(v.profile.id),
                planting_year_month=str(v.plant_month),
                clearance_year_month=str(v.clearance_month),
                harvest_year_months=[HarvestYearMonth(str(hm)) for hm in v.harvest_months],
                expected_revenue_cents=int(v.gross_revenue_cents.real),
                expenses_cents=int(v.expenses_cents.real),
                yield_multiplier=v.yield_multiplier,
                irrigation_applied_mm=v.irrigation_applied_mm,
                nitrogen_applied_kg_ha=v.nitrogen_applied_kg_ha,
                nitrogen_fixed_kg_ha=v.nitrogen_fixed_kg_ha,
                nitrogen_removed_kg_ha=v.nitrogen_removed_kg_ha,
                risk_notes=[str(note) for note in v.risk_notes],
            )
            cycles.append(cycle)

    return OptimizeFieldResponse(
        status=OptimizationResultStatus(result.status.value),
        expected_profit_cents=total_profit_cents,
        schedule=cycles,
    )