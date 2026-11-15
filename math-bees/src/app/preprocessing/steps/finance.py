from __future__ import annotations

from collections import defaultdict
from dataclasses import replace
from typing import Any, Final

from app.models.planning import SolverContext
from app.models.types import (
    CropProfileId,
    DeathNote,
    Note,
    NoteCategory,
    NoteImportance,
    Rate,
    UsdCents,
    Year,
)
from app.preprocessing.candidate import SimulationCandidate
from app.preprocessing.steps.base import SimulationStep

KG_PER_TONNE: Final[float] = 1000.0
WATER_MM_TO_TONS_HA_MULTIPLIER: Final[float] = 10.0
DEFAULT_YIELD_MULTIPLIER: Final[float] = 1.0
ZERO_COST_CENTS: Final[float] = 0.0
ZERO_PROFIT_THRESHOLD_CENTS: Final[float] = 0.0
ZERO_DEPTH_MM: Final[float] = 0.0
ZERO_NUTRIENT_VALUE: Final[float] = 0.0


def _resolve_price_rate(
    material_id: Any | None,
    target_year: Year,
    context: SolverContext,
    candidate: SimulationCandidate,
    price_type: str,
    price_cache: dict[tuple[Any, Year], tuple[Rate | None, str, Year | None]],
) -> tuple[SimulationCandidate, Rate | None]:
    cache_key = (material_id, target_year)
    cached_info = price_cache.get(cache_key)
    if cached_info is None:
        if material_id is None or material_id not in context.material_prices:
            cached_info = (None, "MISSING_DIR", None)
        else:
            year_prices = context.material_prices[material_id]
            if not year_prices:
                cached_info = (None, "EMPTY_DIR", None)
            elif target_year in year_prices:
                cached_info = (year_prices[target_year], "EXACT", None)
            else:
                latest_year = max(year_prices.keys(), key=lambda y: y.real)
                cached_info = (year_prices[latest_year], "FALLBACK", latest_year)
        price_cache[cache_key] = cached_info
    rate, status, fallback_year = cached_info
    if status == "MISSING_DIR":
        cand = candidate.with_risk(
            note=Note(
                NoteCategory.DATA_MISSING,
                NoteImportance.WARN,
                f"Missing {price_type.lower()} pricing data for this crop",
            ),
            fatal_if=not context.risk.allow_missing_data,
            death_note=DeathNote(f"FinanceStep:Missing{price_type}Price"),
        )
        return cand, None
    if status == "EMPTY_DIR":
        cand = candidate.with_risk(
            note=Note(
                NoteCategory.DATA_MISSING,
                NoteImportance.WARN,
                f"The price catalog for '{material_id}' is empty",
            ),
            fatal_if=not context.risk.allow_missing_data,
            death_note=DeathNote(f"FinanceStep:Missing{price_type}Price"),
        )
        return cand, None
    if status == "FALLBACK":
        cand = candidate.with_risk(
            note=Note(
                NoteCategory.DATA_MISSING,
                NoteImportance.INFO,
                f"Missing {price_type.lower()} price for {target_year.real}. "
                f"Falling back to the latest available price from {fallback_year.real if fallback_year else 'previous years'}",
            ),
            fatal_if=False,
            death_note=DeathNote(f"FinanceStep:Missing{price_type}PriceForYear"),
        )
        return cand, rate
    return candidate, rate


def _resolve_profile_opex_rate(
    profile_id: CropProfileId,
    target_year: Year,
    context: SolverContext,
    candidate: SimulationCandidate,
    cache: dict[tuple[CropProfileId, Year], tuple[Rate | None, str, Year | None]],
) -> tuple[SimulationCandidate, Rate | None]:
    cache_key = (profile_id, target_year)
    cached_info = cache.get(cache_key)
    if cached_info is None:
        if profile_id not in context.crop_profile_opexs:
            cached_info = (None, "MISSING_DIR", None)
        else:
            year_rates = context.crop_profile_opexs[profile_id]
            if not year_rates:
                cached_info = (None, "EMPTY_DIR", None)
            elif target_year in year_rates:
                cached_info = (year_rates[target_year], "EXACT", None)
            else:
                latest_year = max(year_rates.keys(), key=lambda y: y.real)
                cached_info = (year_rates[latest_year], "FALLBACK", latest_year)
        cache[cache_key] = cached_info
    rate, status, fallback_year = cached_info
    if status == "MISSING_DIR":
        cand = candidate.with_risk(
            note=Note(
                NoteCategory.DATA_MISSING,
                NoteImportance.WARN,
                "Missing base operational costs (OpEx) for this crop",
            ),
            fatal_if=not context.risk.allow_missing_data,
            death_note=DeathNote("FinanceStep:MissingProfileOpEx"),
        )
        return cand, None
    if status == "FALLBACK":
        cand = candidate.with_risk(
            note=Note(
                NoteCategory.DATA_MISSING,
                NoteImportance.INFO,
                f"Missing operational costs (OpEx) for {target_year.real}. "
                f"Falling back to the rates from {fallback_year.real if fallback_year else 'previous years'}",
            ),
            fatal_if=False,
            death_note=DeathNote("FinanceStep:MissingOpExForYear"),
        )
        return cand, rate
    return candidate, rate


class FinanceStep(SimulationStep):
    def __init__(self) -> None:
        self._price_cache: dict[tuple[Any, Year], tuple[Rate | None, str, Year | None]] = {}
        self._opex_cache: dict[
            tuple[CropProfileId, Year], tuple[Rate | None, str, Year | None]
        ] = {}

    def _calculate_base_opex(
        self,
        candidate: SimulationCandidate,
        context: SolverContext,
        plant_year: Year,
        area_ha: float,
    ) -> tuple[SimulationCandidate, float]:
        candidate, opex_rate = _resolve_profile_opex_rate(
            profile_id=candidate.profile.id,
            target_year=plant_year,
            context=context,
            candidate=candidate,
            cache=self._opex_cache,
        )
        cost = float(opex_rate.real) * area_ha if opex_rate is not None else ZERO_COST_CENTS
        return candidate, cost

    def _calculate_planting_cost(
        self,
        candidate: SimulationCandidate,
        context: SolverContext,
        plant_year: Year,
        area_ha: float,
    ) -> tuple[SimulationCandidate, float]:
        candidate, rate_cents = _resolve_price_rate(
            material_id=candidate.profile.planting_material_id,
            target_year=plant_year,
            context=context,
            candidate=candidate,
            price_type="Planting",
            price_cache=self._price_cache,
        )
        cost = ZERO_COST_CENTS
        if rate_cents is not None:
            seed_rate_tons_ha = float(candidate.profile.planting_rate_kg_per_ha.real) / KG_PER_TONNE
            cost = float(rate_cents.real) * seed_rate_tons_ha * area_ha
        return candidate, cost

    def _calculate_revenue(
        self,
        candidate: SimulationCandidate,
        context: SolverContext,
        area_ha: float,
        *,
        abort_on_death: bool = True,
    ) -> tuple[SimulationCandidate, float]:
        revenue = ZERO_COST_CENTS
        if not candidate.harvest_events:
            return candidate, revenue
        for idx, harvest_date in enumerate(candidate.harvest_events):
            harvest_year = Year(harvest_date.year)
            candidate, rate_cents = _resolve_price_rate(
                material_id=candidate.profile.product_id,
                target_year=harvest_year,
                context=context,
                candidate=candidate,
                price_type="Product",
                price_cache=self._price_cache,
            )
            if candidate.is_dead and abort_on_death:
                break
            if rate_cents is not None:
                multiplier = (
                    candidate.yield_multipliers[idx].real
                    if idx < len(candidate.yield_multipliers)
                    else (
                        candidate.yield_multipliers[-1].real
                        if candidate.yield_multipliers
                        else DEFAULT_YIELD_MULTIPLIER
                    )
                )
                base_yield_tons = float(candidate.profile.potential_yield_tons_per_ha.real)
                revenue += float(rate_cents.real) * base_yield_tons * area_ha * float(multiplier)
        return candidate, revenue

    def _calculate_water_cost(
        self,
        candidate: SimulationCandidate,
        context: SolverContext,
        area_ha: float,
        *,
        material_id: Any | None,
        abort_on_death: bool = True,
    ) -> tuple[SimulationCandidate, float]:
        cost = ZERO_COST_CENTS
        water_by_year: dict[int, float] = defaultdict(float)
        for ym, dynamics in candidate.monthly_water.items():
            if dynamics.applied_mm.real > ZERO_DEPTH_MM:
                water_by_year[ym.year] += float(dynamics.applied_mm.real)
        if not water_by_year:
            return candidate, cost
        for year_int, total_mm in water_by_year.items():
            candidate, rate_cents = _resolve_price_rate(
                material_id=material_id,
                target_year=Year(year_int),
                context=context,
                candidate=candidate,
                price_type="Water",
                price_cache=self._price_cache,
            )
            if candidate.is_dead and abort_on_death:
                break
            if rate_cents is not None:
                water_tons_ha = total_mm * WATER_MM_TO_TONS_HA_MULTIPLIER
                cost += float(rate_cents.real) * area_ha * water_tons_ha
        return candidate, cost

    def _calculate_fertilizer_cost(
        self,
        candidate: SimulationCandidate,
        context: SolverContext,
        plant_year: Year,
        area_ha: float,
        *,
        abort_on_death: bool = True,
    ) -> tuple[SimulationCandidate, float]:
        cost = ZERO_COST_CENTS
        applied_fert = candidate.fertilizer
        if applied_fert is None:
            return candidate, cost
        fert = applied_fert.product
        total_fert_kg_ha = float(applied_fert.rate.real)
        if total_fert_kg_ha <= ZERO_NUTRIENT_VALUE and candidate.monthly_nutrients:
            total_n = sum(
                float(d.n_applied_kg_ha.real) for d in candidate.monthly_nutrients.values()
            )
            total_p = sum(
                float(d.p_applied_kg_ha.real) for d in candidate.monthly_nutrients.values()
            )
            total_k = sum(
                float(d.k_applied_kg_ha.real) for d in candidate.monthly_nutrients.values()
            )
            reqs: list[float] = []
            if fert.n_percent.as_fraction > 0 and total_n > ZERO_NUTRIENT_VALUE:
                reqs.append(total_n / fert.n_percent.as_fraction)
            if fert.p_percent.as_fraction > 0 and total_p > ZERO_NUTRIENT_VALUE:
                reqs.append(total_p / fert.p_percent.as_fraction)
            if fert.k_percent.as_fraction > 0 and total_k > ZERO_NUTRIENT_VALUE:
                reqs.append(total_k / fert.k_percent.as_fraction)
            total_fert_kg_ha = max(reqs) if reqs else ZERO_NUTRIENT_VALUE
        if total_fert_kg_ha > ZERO_NUTRIENT_VALUE:
            active_years_set = {plant_year.real}
            for h_date in candidate.harvest_events:
                active_years_set.add(h_date.year)
            active_years = sorted(active_years_set)
            num_years = len(active_years)
            annual_tons_ha = (total_fert_kg_ha / num_years) / KG_PER_TONNE
            for y_int in active_years:
                candidate, rate_cents = _resolve_price_rate(
                    material_id=fert.id,
                    target_year=Year(y_int),
                    context=context,
                    candidate=candidate,
                    price_type="Fertilizer",
                    price_cache=self._price_cache,
                )
                if candidate.is_dead and abort_on_death:
                    break
                if rate_cents is not None:
                    cost += float(rate_cents.real) * area_ha * annual_tons_ha
        return candidate, cost

    def apply(
        self,
        candidate: SimulationCandidate,
        context: SolverContext,
    ) -> list[SimulationCandidate]:
        if candidate.is_dead:
            return [candidate]
        area_ha = float(candidate.field.area_ha.real)
        plant_year = Year(candidate.plant_date.year)

        candidate, opex_cents = self._calculate_base_opex(candidate, context, plant_year, area_ha)
        if candidate.is_dead:
            return [candidate]
        candidate, planting_cents = self._calculate_planting_cost(
            candidate, context, plant_year, area_ha
        )
        if candidate.is_dead:
            return [candidate]
        candidate, revenue_cents = self._calculate_revenue(candidate, context, area_ha)
        if candidate.is_dead:
            return [candidate]

        irrigation = context.replenishment.irrigation
        water_cents = ZERO_COST_CENTS
        if irrigation.apply and irrigation.material_id:
            candidate, water_cents = self._calculate_water_cost(
                candidate, context, area_ha, material_id=irrigation.material_id
            )
            if candidate.is_dead:
                return [candidate]

        fert_cents = ZERO_COST_CENTS
        if context.replenishment.fertilizers.apply and candidate.monthly_nutrients:
            candidate, fert_cents = self._calculate_fertilizer_cost(
                candidate, context, plant_year, area_ha
            )
            if candidate.is_dead:
                return [candidate]

        final_candidate = replace(
            candidate,
            gross_revenue_cents=UsdCents(int(revenue_cents)),
            base_opex_cents=UsdCents(int(opex_cents)),
            planting_cost_cents=UsdCents(int(planting_cents)),
            water_cost_cents=UsdCents(int(water_cents)),
            fertilizer_cost_cents=UsdCents(int(fert_cents)),
        )
        pure_cash_profit_cents = (
            final_candidate.gross_revenue_cents.real - final_candidate.expenses_cents.real
        )
        if pure_cash_profit_cents <= ZERO_PROFIT_THRESHOLD_CENTS:
            total_n_fixed = sum(
                float(dyn.n_fixed_kg_ha.real) for dyn in final_candidate.monthly_nutrients.values()
            )
            total_n_applied = sum(
                float(dyn.n_applied_kg_ha.real)
                for dyn in final_candidate.monthly_nutrients.values()
            )
            total_n_removed = sum(
                float(dyn.n_removed_kg_ha.real)
                for dyn in final_candidate.monthly_nutrients.values()
            )
            has_nitrogen_balance = (total_n_fixed + total_n_applied) >= total_n_removed
            has_fertilizer_applied = final_candidate.fertilizer is not None and (
                total_n_applied > 0
            )
            if not (
                has_nitrogen_balance
                or final_candidate.profile.is_perennial
                or has_fertilizer_applied
            ):
                return [
                    final_candidate.killed(
                        DeathNote("FinanceStep:UnprofitableWithoutBenefit", is_static=False),
                    ),
                ]
        return [final_candidate]


class ScenarioFinanceStep(FinanceStep):
    def apply(
        self,
        candidate: SimulationCandidate,
        context: SolverContext,
    ) -> list[SimulationCandidate]:
        if not candidate.stage_offsets:
            return [candidate]

        area_ha = float(candidate.field.area_ha.real)
        plant_year = Year(candidate.plant_date.year)
        water_material_id = (
            candidate.irrigation_application.material_id
            if candidate.irrigation_application is not None
            else None
        )

        candidate, opex_cents = self._calculate_base_opex(candidate, context, plant_year, area_ha)
        candidate, planting_cents = self._calculate_planting_cost(
            candidate, context, plant_year, area_ha
        )
        candidate, revenue_cents = self._calculate_revenue(
            candidate, context, area_ha, abort_on_death=False
        )
        candidate, water_cents = self._calculate_water_cost(
            candidate,
            context,
            area_ha,
            material_id=water_material_id,
            abort_on_death=False,
        )
        candidate, fert_cents = self._calculate_fertilizer_cost(
            candidate, context, plant_year, area_ha, abort_on_death=False
        )

        return [
            replace(
                candidate,
                gross_revenue_cents=UsdCents(int(revenue_cents)),
                base_opex_cents=UsdCents(int(opex_cents)),
                planting_cost_cents=UsdCents(int(planting_cents)),
                water_cost_cents=UsdCents(int(water_cents)),
                fertilizer_cost_cents=UsdCents(int(fert_cents)),
            )
        ]
