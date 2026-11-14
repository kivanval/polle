from __future__ import annotations

from typing import Any

from app.autogen.api.schemas import (
    Category,
    CropNote,
    CropProfileCycle,
    CropProfileCycleFinancials,
    CropProfileCycleMonthlyDynamics,
    EvaluateScenarioRequest,
    EvaluateScenarioResponse,
    FertilizerApplication,
    Importance,
    OptimizeFieldRequest,
    OptimizeFieldResponse,
    RiskSettings,
    SolverResultStatus,
)
from app.models.planning import (
    CropScheduleItem,
    Horizon,
    PlantingVariant,
    ScheduledFertilizerApplication,
    ScheduledIrrigationApplication,
)
from app.models.types import CropProfileId, FieldId, Kilogram, MaterialId, Rate, WaterMm
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
    if resolution_enum is None:
        return (0.0,)

    try:
        resolution_val = str(resolution_enum.value)
    except AttributeError:
        resolution_val = str(resolution_enum)

    return _RESOLUTION_LEVELS.get(resolution_val, (0.0,))


def _map_risk_settings(risk_dto: RiskSettings | None) -> RiskSetting:
    if risk_dto is None:
        return RiskSetting(
            allow_missing_data=True,
            allow_agronomic_risks=True,
            allow_soil_depletion=True,
        )

    return RiskSetting(
        allow_missing_data=(
            bool(risk_dto.allow_missing_data) if risk_dto.allow_missing_data is not None else True
        ),
        allow_agronomic_risks=(
            bool(risk_dto.allow_agronomic_risks)
            if risk_dto.allow_agronomic_risks is not None
            else True
        ),
        allow_soil_depletion=(
            bool(risk_dto.allow_soil_depletion)
            if risk_dto.allow_soil_depletion is not None
            else True
        ),
    )


def map_request_to_domain(
    request: OptimizeFieldRequest,
) -> tuple[FieldId, Horizon, SolverStrategy]:
    field_id = FieldId(request.field_id)
    horizon = Horizon(
        start=_parse_year_month(request.horizon.start_year_month),
        end=_parse_year_month(request.horizon.end_year_month),
    )
    strategy_settings = request.strategy

    risk = _map_risk_settings(strategy_settings.risk if strategy_settings else None)

    repl_dto = strategy_settings.replenishment if strategy_settings else None
    irr_dto = repl_dto.irrigation if repl_dto else None
    fert_dto = repl_dto.fertilizers if repl_dto else None

    if irr_dto and irr_dto.apply:
        irr_apply = True
        irr_mat_id = (
            MaterialId(str(irr_dto.material_id)) if irr_dto.material_id is not None else None
        )
        irr_levels = _resolve_levels(irr_dto.resolution)
    else:
        irr_apply = False
        irr_mat_id = None
        irr_levels = (0.0,)

    irrigation = IrrigationSetting(
        apply=irr_apply,
        material_id=irr_mat_id,
        levels=irr_levels,
    )

    if fert_dto and fert_dto.apply:
        fert_apply = True
        fert_levels = _resolve_levels(fert_dto.resolution)
    else:
        fert_apply = False
        fert_levels = (0.0,)

    fertilizers = FertilizerSetting(
        apply=fert_apply,
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


def map_evaluate_request_to_domain(
    request: EvaluateScenarioRequest,
) -> tuple[FieldId, Horizon, SolverStrategy, list[CropScheduleItem]]:
    field_id = FieldId(request.field_id)
    horizon = Horizon(
        start=_parse_year_month(request.horizon.start_year_month),
        end=_parse_year_month(request.horizon.end_year_month),
    )

    risk = _map_risk_settings(request.risk)

    schedule: list[CropScheduleItem] = []
    has_irrigation = False
    has_fertilizer = False

    for crop in request.schedule:
        fert_app = (
            ScheduledFertilizerApplication(
                material_id=MaterialId(str(crop.fertilizer_application.material_id)),
                rate_kg_ha=Rate(
                    real=float(crop.fertilizer_application.rate_kg_ha),
                    target_type=Kilogram,
                ),
            )
            if crop.fertilizer_application is not None
            else None
        )
        irr_app = (
            ScheduledIrrigationApplication(
                material_id=MaterialId(str(crop.irrigation_application.material_id)),
                amount_mm=WaterMm(float(crop.irrigation_application.amount_mm)),
            )
            if crop.irrigation_application is not None
            else None
        )

        if irr_app is not None and float(irr_app.amount_mm.real) > 0:
            has_irrigation = True
        if fert_app is not None and float(fert_app.rate_kg_ha.real) > 0:
            has_fertilizer = True

        schedule.append(
            CropScheduleItem(
                profile_id=CropProfileId(str(crop.crop_profile_id)),
                plant_date=crop.plant_date,
                clearance_date=crop.clearance_date,
                fertilizer_application=fert_app,
                irrigation_application=irr_app,
            )
        )

    irrigation = IrrigationSetting(
        apply=has_irrigation,
        material_id=None,
        levels=(1.0,) if has_irrigation else (0.0,),
    )

    fertilizers = FertilizerSetting(
        apply=has_fertilizer,
        levels=(1.0,) if has_fertilizer else (0.0,),
    )

    replenishment = ReplenishmentSetting(
        irrigation=irrigation,
        fertilizers=fertilizers,
    )

    strategy = SolverStrategy(
        risk=risk,
        replenishment=replenishment,
    )

    return field_id, horizon, strategy, schedule


def _format_float_number(number: Any) -> float:
    try:
        val = number.real
    except AttributeError:
        val = number
    return round(float(val), 2)


def _map_to_crop_profile(variant: PlantingVariant) -> CropProfileCycle:
    monthly_dynamics = CropProfileCycleMonthlyDynamics(
        water_applied_mm={
            str(ym): _format_float_number(w.applied_mm) for ym, w in variant.monthly_water.items()
        },
        water_removed_mm={
            str(ym): _format_float_number(w.removed_mm) for ym, w in variant.monthly_water.items()
        },
        nitrogen_applied_kg_ha={
            str(ym): _format_float_number(n.n_applied_kg_ha)
            for ym, n in variant.monthly_nutrients.items()
        },
        nitrogen_fixed_kg_ha={
            str(ym): _format_float_number(n.n_fixed_kg_ha)
            for ym, n in variant.monthly_nutrients.items()
        },
        nitrogen_removed_kg_ha={
            str(ym): _format_float_number(n.n_removed_kg_ha)
            for ym, n in variant.monthly_nutrients.items()
        },
        phosphorus_applied_kg_ha={
            str(ym): _format_float_number(n.p_applied_kg_ha)
            for ym, n in variant.monthly_nutrients.items()
        },
        phosphorus_removed_kg_ha={
            str(ym): _format_float_number(n.p_removed_kg_ha)
            for ym, n in variant.monthly_nutrients.items()
        },
        potassium_applied_kg_ha={
            str(ym): _format_float_number(n.k_applied_kg_ha)
            for ym, n in variant.monthly_nutrients.items()
        },
        potassium_removed_kg_ha={
            str(ym): _format_float_number(n.k_removed_kg_ha)
            for ym, n in variant.monthly_nutrients.items()
        },
    )

    fertilizer_app = (
        FertilizerApplication(
            material_id=str(variant.fertilizer.product.id),
            rate_kg_ha=float(variant.fertilizer.rate.real),
        )
        if variant.fertilizer is not None
        else None
    )

    financials = CropProfileCycleFinancials(
        gross_revenue_cents=int(variant.gross_revenue_cents.real),
        base_opex_cents=int(variant.base_opex_cents.real),
        planting_cost_cents=int(variant.planting_cost_cents.real),
        water_cost_cents=int(variant.water_cost_cents.real),
        fertilizer_cost_cents=int(variant.fertilizer_cost_cents.real),
    )

    return CropProfileCycle(
        crop_profile_id=str(variant.profile.id),
        plant_date=variant.plant_date,
        clearance_date=variant.clearance_date,
        harvest_dates=list(variant.harvest_events),
        financials=financials,
        yield_multiplier=float(variant.yield_multipliers[-1].real)
        if variant.yield_multipliers
        else 1.0,
        fertilizer_application=fertilizer_app,
        monthly_dynamics=monthly_dynamics,
        notes=[
            CropNote(
                importance=Importance(note.importance.lower()),
                category=Category(note.category.lower()),
                message=note.message,
            )
            for note in variant.notes
        ],
    )


def map_result_to_response(result: SolverResult) -> OptimizeFieldResponse:
    cycles: list[CropProfileCycle] = []

    if result.is_feasible and result.schedule:
        for v in result.schedule.chronological():
            cycle = _map_to_crop_profile(v)
            cycles.append(cycle)

    return OptimizeFieldResponse(
        status=SolverResultStatus(result.status.value),
        schedule=cycles,
    )


def map_evaluate_result_to_response(result: SolverResult) -> EvaluateScenarioResponse:
    cycles: list[CropProfileCycle] = []

    if result.schedule:
        for v in result.schedule.chronological():
            cycle = _map_to_crop_profile(v)
            cycles.append(cycle)

    return EvaluateScenarioResponse(
        status=SolverResultStatus(result.status.value),
        schedule=cycles,
    )
