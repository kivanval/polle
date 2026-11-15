from __future__ import annotations

from collections import Counter
from collections.abc import Sequence
from dataclasses import replace
from typing import Final

from app.models.agronomy import AppliedFertilizer, Fertilizer, SoilImpact
from app.models.planning import SolverContext
from app.models.types import (
    AreaHa,
    DeathNote,
    Kilogram,
    MonthlyNutrientDynamics,
    Note,
    NoteCategory,
    NoteImportance,
    Percent,
    Rate,
    SoilTexture,
    Year,
    YieldMultiplier,
)
from app.models.year_month import YearMonth
from app.preprocessing.candidate import SimulationCandidate
from app.preprocessing.steps.base import SimulationStep

CRITICAL_NUTRIENT_YIELD_THRESHOLD: Final[float] = 0.75
FULL_YIELD_MULTIPLIER: Final[float] = 1.0
ZERO_NUTRIENT_VALUE: Final[float] = 0.0
ZERO_COST_CENTS: Final[float] = 0.0

MAX_FERTILIZER_BNF_SUPPRESSION: Final[float] = 0.80
MIN_RESIDUAL_BNF_FRACTION: Final[float] = 1.0 - MAX_FERTILIZER_BNF_SUPPRESSION

RATE_ROUND_DECIMALS: Final[int] = 2
YIELD_MULTIPLIER_ROUND_DECIMALS: Final[int] = 3
PERCENTAGE_MULTIPLIER: Final[float] = 100.0

_TEXTURE_BASE_YIELD: Final[dict[SoilTexture, float]] = {
    SoilTexture.SAND: 0.30,
    SoilTexture.LOAMY_SAND: 0.35,
    SoilTexture.SANDY_LOAM: 0.45,
    SoilTexture.LOAM: 0.60,
    SoilTexture.SILT_LOAM: 0.65,
    SoilTexture.CLAY_LOAM: 0.60,
    SoilTexture.CLAY: 0.55,
    SoilTexture.PEAT: 0.50,
}
DEFAULT_BASE_YIELD: Final[float] = 0.50

ZERO_RATE_KG_HA: Final[Rate[Kilogram, AreaHa]] = Rate(real=0.0, target_type=Kilogram)


def _rate_kg_ha(val: float) -> Rate[Kilogram, AreaHa]:
    if val == 0.0:
        return ZERO_RATE_KG_HA
    rounded = round(val, RATE_ROUND_DECIMALS)
    if rounded == 0.0:
        return ZERO_RATE_KG_HA
    return Rate(real=rounded, target_type=Kilogram)


class NutrientBalanceStep(SimulationStep):
    def __init__(self):
        super().__init__()
        self._fert_cache: dict[tuple[Year, bool, bool, bool], list[Fertilizer]] = {}

    def apply(
        self,
        candidate: SimulationCandidate,
        context: SolverContext,
    ) -> list[SimulationCandidate]:
        if candidate.is_dead:
            return [candidate]

        candidate, has_missing_data = self._check_removal_rates(
            candidate, context, abort_on_fatal=True
        )
        if candidate.is_dead:
            return [candidate]

        (
            n_removal,
            p_removal,
            k_removal,
            n_deficit,
            p_deficit,
            k_deficit,
            max_n_fixation,
            max_n_fix_fraction,
        ) = self._calculate_base_requirements(candidate)

        if context.replenishment.fertilizers.apply:
            plant_year = Year(candidate.plant_date.year)
            cache_key = (plant_year, n_deficit > 0, p_deficit > 0, k_deficit > 0)
            if cache_key not in self._fert_cache:
                self._fert_cache[cache_key] = self._filter_best_fertilizers(
                    context, plant_year, n_deficit, p_deficit, k_deficit
                )
            valid_fertilizers = self._fert_cache[cache_key]
        else:
            valid_fertilizers = []

        return self._generate_fertilizer_variants(
            candidate=candidate,
            context=context,
            valid_fertilizers=valid_fertilizers,
            n_deficit=n_deficit,
            p_deficit=p_deficit,
            k_deficit=k_deficit,
            n_removal=n_removal,
            p_removal=p_removal,
            k_removal=k_removal,
            max_n_fixation=max_n_fixation,
            max_n_fix_fraction=max_n_fix_fraction,
            has_missing_data=has_missing_data,
        )

    @staticmethod
    def _check_removal_rates(
        candidate: SimulationCandidate,
        context: SolverContext,
        *,
        abort_on_fatal: bool = True,
    ) -> tuple[SimulationCandidate, bool]:
        profile = candidate.profile
        n_rate, p_rate, k_rate = (
            profile.n_removal_kg_per_unit,
            profile.p_removal_kg_per_unit,
            profile.k_removal_kg_per_unit,
        )

        if n_rate is not None and p_rate is not None and k_rate is not None:
            return candidate, False

        missing_elements = [
            elem for elem, rate in (("N", n_rate), ("P", p_rate), ("K", k_rate)) if rate is None
        ]
        elem_list = ", ".join(missing_elements)
        elements_slug = "-".join(missing_elements)

        cand = candidate.with_risk(
            Note(
                NoteCategory.DATA_MISSING,
                NoteImportance.RISK,
                f"Missing nutrient removal rates for: {elem_list}",
            ),
            fatal_if=abort_on_fatal and not context.risk.allow_missing_data,
            death_note=DeathNote(
                f"NutrientBalance:MissingRemoval({elements_slug})",
                is_static=True,
            ),
        )
        return cand, True

    @staticmethod
    def _calculate_base_requirements(
        candidate: SimulationCandidate,
    ) -> tuple[float, float, float, float, float, float, float, float]:
        profile = candidate.profile
        harvest_count = max(1, len(candidate.harvest_events))
        base_yield = float(profile.potential_yield_tons_per_ha.real)
        effective_yield_t_ha: float = base_yield * harvest_count

        n_rate = profile.n_removal_kg_per_unit
        p_rate = profile.p_removal_kg_per_unit
        k_rate = profile.k_removal_kg_per_unit

        base_n_fixation_rate = (
            float(profile.n_fixation_kg_per_unit.real)
            if profile.n_fixation_kg_per_unit is not None
            else ZERO_NUTRIENT_VALUE
        )
        max_n_fixation = base_n_fixation_rate * effective_yield_t_ha

        max_n_fix_fraction = (
            float(profile.max_n_fixation_fraction.real)
            if profile.max_n_fixation_fraction is not None
            else ZERO_NUTRIENT_VALUE
        )

        if n_rate is not None and p_rate is not None and k_rate is not None:
            n_removal = effective_yield_t_ha * float(n_rate.real)
            p_removal = effective_yield_t_ha * float(p_rate.real)
            k_removal = effective_yield_t_ha * float(k_rate.real)

            if max_n_fix_fraction > 0 and n_removal > 0:
                max_n_fixation = min(max_n_fixation, n_removal * max_n_fix_fraction)

            if 0 < max_n_fixation < n_removal and n_removal > 0:
                denominator = 1.0 - (MAX_FERTILIZER_BNF_SUPPRESSION * (max_n_fixation / n_removal))
                n_deficit = (n_removal - max_n_fixation) / denominator
            else:
                n_deficit = max(ZERO_NUTRIENT_VALUE, n_removal - max_n_fixation)

            p_deficit = p_removal
            k_deficit = k_removal
        else:
            n_removal = p_removal = k_removal = ZERO_NUTRIENT_VALUE
            n_deficit = p_deficit = k_deficit = ZERO_NUTRIENT_VALUE

        return (
            n_removal,
            p_removal,
            k_removal,
            n_deficit,
            p_deficit,
            k_deficit,
            max_n_fixation,
            max_n_fix_fraction,
        )

    @staticmethod
    def _filter_best_fertilizers(
        context: SolverContext,
        plant_year: Year,
        n_deficit: float,
        p_deficit: float,
        k_deficit: float,
    ) -> list[Fertilizer]:
        valid_fertilizers = [
            fert
            for fert in context.fertilizers
            if (n_deficit <= 0 or fert.n_percent.as_fraction > 0)
            and (p_deficit <= 0 or fert.p_percent.as_fraction > 0)
            and (k_deficit <= 0 or fert.k_percent.as_fraction > 0)
        ]

        if not valid_fertilizers and context.fertilizers:
            valid_fertilizers = context.fertilizers

        if not valid_fertilizers:
            return []

        best_by_profile: dict[tuple[bool, bool, bool, SoilImpact], tuple[Fertilizer, float]] = {}

        for fert in valid_fertilizers:
            n_frac = fert.n_percent.as_fraction
            p_frac = fert.p_percent.as_fraction
            k_frac = fert.k_percent.as_fraction
            max_frac = max(n_frac, p_frac, k_frac)

            if max_frac <= 0.0:
                continue

            price_dict = context.material_prices.get(fert.id, {})

            if not price_dict:
                if context.risk.allow_missing_data:
                    price = ZERO_COST_CENTS
                else:
                    continue
            else:
                if plant_year in price_dict:
                    price = float(price_dict[plant_year].real)
                else:
                    latest_year = max(price_dict.keys(), key=lambda y: y.real)
                    price = float(price_dict[latest_year].real)

            if price is None:
                continue

            effective_price = price / max_frac
            profile_key = (n_frac > 0, p_frac > 0, k_frac > 0, fert.soil_impact)

            if (
                profile_key not in best_by_profile
                or effective_price < best_by_profile[profile_key][1]
            ):
                best_by_profile[profile_key] = (fert, effective_price)

        return [f for f, _p in best_by_profile.values()]

    @staticmethod
    def _generate_fertilizer_variants(
        candidate: SimulationCandidate,
        context: SolverContext,
        valid_fertilizers: list[Fertilizer],
        n_deficit: float,
        p_deficit: float,
        k_deficit: float,
        n_removal: float,
        p_removal: float,
        k_removal: float,
        max_n_fixation: float,
        max_n_fix_fraction: float,
        has_missing_data: bool,
    ) -> list[SimulationCandidate]:
        generated_variants: list[SimulationCandidate] = []
        fertilizer_settings = context.replenishment.fertilizers
        raw_texture_baseline = _TEXTURE_BASE_YIELD.get(
            candidate.field.soil.texture, DEFAULT_BASE_YIELD
        )

        total_deficit = n_deficit + p_deficit + k_deficit
        levels_to_process = (
            fertilizer_settings.levels if total_deficit > ZERO_NUTRIENT_VALUE else [0.0]
        )

        for level in levels_to_process:
            options: Sequence[Fertilizer | None] = (
                (None,)
                if level == 0.0 or not fertilizer_settings.apply or not context.fertilizers
                else valid_fertilizers
            )

            if not options and level == 0.0:
                options = (None,)

            for fert_product in options:
                if fert_product is not None and level > 0.0:
                    n_frac = fert_product.n_percent.as_fraction
                    p_frac = fert_product.p_percent.as_fraction
                    k_frac = fert_product.k_percent.as_fraction

                    will_apply = (
                        (n_deficit > 0 and n_frac > 0)
                        or (p_deficit > 0 and p_frac > 0)
                        or (k_deficit > 0 and k_frac > 0)
                    )
                    if not will_apply:
                        continue

                active_product = fert_product if level > 0.0 else None
                curr_cand = NutrientBalanceStep._apply_fertilizer_notes(
                    candidate=candidate,
                    fert_product=active_product,
                    n_deficit=n_deficit,
                    p_deficit=p_deficit,
                    k_deficit=k_deficit,
                )

                applied_fertilizer: AppliedFertilizer | None = None
                total_n_app = ZERO_NUTRIENT_VALUE
                total_p_app = ZERO_NUTRIENT_VALUE
                total_k_app = ZERO_NUTRIENT_VALUE

                if active_product is not None:
                    n_frac = active_product.n_percent.as_fraction
                    p_frac = active_product.p_percent.as_fraction
                    k_frac = active_product.k_percent.as_fraction

                    rates: list[float] = []
                    if n_deficit > 0 and n_frac > 0:
                        rates.append((n_deficit * level) / n_frac)
                    if p_deficit > 0 and p_frac > 0:
                        rates.append((p_deficit * level) / p_frac)
                    if k_deficit > 0 and k_frac > 0:
                        rates.append((k_deficit * level) / k_frac)

                    target_rate_kg_ha = max(rates) if rates else ZERO_NUTRIENT_VALUE

                    if target_rate_kg_ha > ZERO_NUTRIENT_VALUE:
                        applied_fertilizer = AppliedFertilizer(
                            product=active_product,
                            rate=_rate_kg_ha(target_rate_kg_ha),
                            target_percentage=Percent.from_fraction(float(level)),
                        )
                        total_n_app = target_rate_kg_ha * n_frac
                        total_p_app = target_rate_kg_ha * p_frac
                        total_k_app = target_rate_kg_ha * k_frac

                is_unfertilized = level == 0.0 or not context.replenishment.fertilizers.apply
                curr_cand, fert_yield_mult, actual_n_fixation = (
                    NutrientBalanceStep._compute_nutrition_response(
                        candidate=curr_cand,
                        context=context,
                        fert_product=active_product,
                        total_n_app=total_n_app,
                        total_p_app=total_p_app,
                        total_k_app=total_k_app,
                        n_removal=n_removal,
                        p_removal=p_removal,
                        k_removal=k_removal,
                        p_deficit=p_deficit,
                        k_deficit=k_deficit,
                        max_n_fixation=max_n_fixation,
                        max_n_fix_fraction=max_n_fix_fraction,
                        raw_texture_baseline=raw_texture_baseline,
                        is_unfertilized=is_unfertilized,
                        level_pct=round(level * PERCENTAGE_MULTIPLIER),
                        abort_on_fatal=True,
                    )
                )

                if curr_cand.is_dead:
                    continue

                variant = NutrientBalanceStep._finalize_nutrition_candidate(
                    candidate=curr_cand,
                    fert_yield_mult=fert_yield_mult,
                    actual_n_fixation=actual_n_fixation,
                    applied_fertilizer=applied_fertilizer,
                    total_n_app=total_n_app,
                    total_p_app=total_p_app,
                    total_k_app=total_k_app,
                    n_removal=n_removal,
                    p_removal=p_removal,
                    k_removal=k_removal,
                    has_missing_data=has_missing_data,
                )
                generated_variants.append(variant)

        return generated_variants

    @staticmethod
    def _apply_fertilizer_notes(
        candidate: SimulationCandidate,
        fert_product: Fertilizer | None,
        n_deficit: float,
        p_deficit: float,
        k_deficit: float,
    ) -> SimulationCandidate:
        if fert_product is None:
            return candidate

        n_frac = fert_product.n_percent.as_fraction
        p_frac = fert_product.p_percent.as_fraction
        k_frac = fert_product.k_percent.as_fraction

        new_notes = []
        if n_deficit > 0 and n_frac == 0:
            new_notes.append(
                Note(
                    NoteCategory.AGRONOMIC,
                    NoteImportance.WARN,
                    "The selected fertilizer does not provide enough Nitrogen",
                )
            )
        if p_deficit > 0 and p_frac == 0:
            new_notes.append(
                Note(
                    NoteCategory.AGRONOMIC,
                    NoteImportance.WARN,
                    "The selected fertilizer does not provide enough Phosphorus",
                )
            )
        if k_deficit > 0 and k_frac == 0:
            new_notes.append(
                Note(
                    NoteCategory.AGRONOMIC,
                    NoteImportance.WARN,
                    "The selected fertilizer does not provide enough Potassium",
                )
            )

        if fert_product.soil_impact == SoilImpact.ACIDIFIES:
            new_notes.append(
                Note(
                    NoteCategory.SOIL_HEALTH,
                    NoteImportance.INFO,
                    f"Applying {fert_product.id} will make the soil more acidic over time",
                )
            )
        elif fert_product.soil_impact == SoilImpact.ALKALINIZES:
            new_notes.append(
                Note(
                    NoteCategory.SOIL_HEALTH,
                    NoteImportance.INFO,
                    f"Applying {fert_product.id} will make the soil more alkaline over time",
                )
            )

        cand = candidate
        for note in new_notes:
            cand = cand.with_risk(note)

        return cand

    @staticmethod
    def _compute_nutrition_response(
        candidate: SimulationCandidate,
        context: SolverContext,
        fert_product: Fertilizer | None,
        total_n_app: float,
        total_p_app: float,
        total_k_app: float,
        n_removal: float,
        p_removal: float,
        k_removal: float,
        p_deficit: float,
        k_deficit: float,
        max_n_fixation: float,
        max_n_fix_fraction: float,
        raw_texture_baseline: float,
        is_unfertilized: bool,
        level_pct: int | None,
        *,
        abort_on_fatal: bool = True,
    ) -> tuple[SimulationCandidate, float, float]:
        actual_n_fixation = ZERO_NUTRIENT_VALUE
        if max_n_fixation > ZERO_NUTRIENT_VALUE:
            n_fert_suppression = 1.0
            if fert_product and fert_product.n_percent.as_fraction > 0 and n_removal > 0:
                n_coverage_ratio = min(1.0, total_n_app / n_removal)
                n_fert_suppression = max(
                    MIN_RESIDUAL_BNF_FRACTION,
                    1.0 - (MAX_FERTILIZER_BNF_SUPPRESSION * n_coverage_ratio),
                )

            actual_n_fixation = max_n_fixation * n_fert_suppression

            if max_n_fix_fraction > 0 and n_removal > 0:
                actual_n_fixation = min(actual_n_fixation, n_removal * max_n_fix_fraction)

        n_factor = FULL_YIELD_MULTIPLIER
        if n_removal > ZERO_NUTRIENT_VALUE:
            n_satisfied_ratio = min(1.0, (actual_n_fixation + total_n_app) / n_removal)
            n_factor = raw_texture_baseline + (1.0 - raw_texture_baseline) * n_satisfied_ratio

        p_factor = FULL_YIELD_MULTIPLIER
        if p_removal > ZERO_NUTRIENT_VALUE:
            p_satisfied_ratio = min(1.0, total_p_app / p_removal) if p_deficit > 0 else 1.0
            p_factor = raw_texture_baseline + (1.0 - raw_texture_baseline) * p_satisfied_ratio

        k_factor = FULL_YIELD_MULTIPLIER
        if k_removal > ZERO_NUTRIENT_VALUE:
            k_satisfied_ratio = min(1.0, total_k_app / k_removal) if k_deficit > 0 else 1.0
            k_factor = raw_texture_baseline + (1.0 - raw_texture_baseline) * k_satisfied_ratio

        # Resolve final yield multiplier via Liebig's Law of the Minimum:
        # The most limiting nutrient (N, P, or K) dictates the overall growth factor.
        fert_yield_mult = min(n_factor, p_factor, k_factor)

        if actual_n_fixation > ZERO_NUTRIENT_VALUE and fert_yield_mult < FULL_YIELD_MULTIPLIER:
            actual_n_fixation = actual_n_fixation * fert_yield_mult

        cand = candidate
        yield_pct = round(fert_yield_mult * PERCENTAGE_MULTIPLIER)

        if yield_pct < 100:
            is_critical = fert_yield_mult <= CRITICAL_NUTRIENT_YIELD_THRESHOLD

            if is_unfertilized:
                msg = f"Without fertilizer, nutrient deficiency limited the yield to {yield_pct}%"
                importance = NoteImportance.WARN
                fatal = False
            elif level_pct is not None and level_pct < 100:
                msg = f"Applying only {level_pct}% of the required fertilizer limited the yield to {yield_pct}%"
                importance = NoteImportance.RISK
                fatal = abort_on_fatal and is_critical and not context.risk.allow_agronomic_risks
            else:
                msg = f"Insufficient fertilizer application limited the yield to {yield_pct}%"
                importance = NoteImportance.RISK
                fatal = abort_on_fatal and is_critical and not context.risk.allow_agronomic_risks

            cand = cand.with_risk(
                Note(NoteCategory.AGRONOMIC, importance, msg),
                fatal_if=fatal,
                death_note=DeathNote(
                    f"NutrientBalance:CriticalDeficit({yield_pct}%)", is_static=False
                ),
            )

        return cand, fert_yield_mult, actual_n_fixation

    @staticmethod
    def _finalize_nutrition_candidate(
        candidate: SimulationCandidate,
        fert_yield_mult: float,
        actual_n_fixation: float,
        applied_fertilizer: AppliedFertilizer | None,
        total_n_app: float,
        total_p_app: float,
        total_k_app: float,
        n_removal: float,
        p_removal: float,
        k_removal: float,
        has_missing_data: bool,
    ) -> SimulationCandidate:
        start_ym = YearMonth(candidate.plant_date.year, candidate.plant_date.month)
        clearance_date = candidate.clearance_date or (
            candidate.harvest_events[-1] if candidate.harvest_events else candidate.plant_date
        )
        end_ym = YearMonth(clearance_date.year, clearance_date.month)

        active_months: list[YearMonth] = []
        curr_ym = start_ym
        while curr_ym <= end_ym:
            active_months.append(curr_ym)
            curr_ym += 1

        active_count = len(active_months)
        harvest_events_count = max(1, len(candidate.harvest_events))
        harvest_months_counts = Counter(
            YearMonth(hd.year, hd.month) for hd in candidate.harvest_events
        )

        base_mult = (
            float(candidate.yield_multipliers[-1].real)
            if candidate.yield_multipliers
            else FULL_YIELD_MULTIPLIER
        )

        if candidate.yield_multipliers and len(candidate.yield_multipliers) == harvest_events_count:
            new_mults = tuple(
                YieldMultiplier(
                    round(float(m.real) * fert_yield_mult, YIELD_MULTIPLIER_ROUND_DECIMALS)
                )
                for m in candidate.yield_multipliers
            )
        else:
            final_mult = YieldMultiplier(
                round(base_mult * fert_yield_mult, YIELD_MULTIPLIER_ROUND_DECIMALS)
            )
            new_mults = tuple(final_mult for _ in range(harvest_events_count))

        mean_crop_mult = sum(float(m.real) for m in new_mults) / len(new_mults)

        actual_n_removal = n_removal * mean_crop_mult
        actual_p_removal = p_removal * mean_crop_mult
        actual_k_removal = k_removal * mean_crop_mult
        scaled_n_fixation = actual_n_fixation * (
            base_mult if base_mult < FULL_YIELD_MULTIPLIER else 1.0
        )

        monthly_nutrients = NutrientBalanceStep._distribute_monthly_dynamics(
            total_n_app=total_n_app,
            total_p_app=total_p_app,
            total_k_app=total_k_app,
            n_removal=actual_n_removal,
            p_removal=actual_p_removal,
            k_removal=actual_k_removal,
            actual_n_fixation=scaled_n_fixation,
            has_missing_data=has_missing_data,
            active_months=active_months,
            harvest_months_counts=harvest_months_counts,
            start_ym=start_ym,
            active_count=active_count,
            harvest_events_count=harvest_events_count,
        )

        return replace(
            candidate,
            yield_multipliers=new_mults,
            monthly_nutrients=monthly_nutrients,
            fertilizer=applied_fertilizer,
        )

    @staticmethod
    def _distribute_monthly_dynamics(
        total_n_app: float,
        total_p_app: float,
        total_k_app: float,
        n_removal: float,
        p_removal: float,
        k_removal: float,
        actual_n_fixation: float,
        has_missing_data: bool,
        active_months: list[YearMonth],
        harvest_months_counts: Counter[YearMonth],
        start_ym: YearMonth,
        active_count: int,
        harvest_events_count: int,
    ) -> dict[YearMonth, MonthlyNutrientDynamics]:
        if has_missing_data:
            return {}

        n_fix_per_month = (
            actual_n_fixation / active_count if active_count > 0 else ZERO_NUTRIENT_VALUE
        )

        if harvest_events_count > 0:
            n_rem_per_harvest = n_removal / harvest_events_count
            p_rem_per_harvest = p_removal / harvest_events_count
            k_rem_per_harvest = k_removal / harvest_events_count
        else:
            n_rem_per_harvest = p_rem_per_harvest = k_rem_per_harvest = ZERO_NUTRIENT_VALUE

        monthly_nutrients: dict[YearMonth, MonthlyNutrientDynamics] = {}

        for ym in active_months:
            h_count = harvest_months_counts.get(ym, 0)
            is_planting_month = ym == start_ym

            monthly_nutrients[ym] = MonthlyNutrientDynamics(
                n_applied_kg_ha=_rate_kg_ha(
                    total_n_app if is_planting_month else ZERO_NUTRIENT_VALUE
                ),
                p_applied_kg_ha=_rate_kg_ha(
                    total_p_app if is_planting_month else ZERO_NUTRIENT_VALUE
                ),
                k_applied_kg_ha=_rate_kg_ha(
                    total_k_app if is_planting_month else ZERO_NUTRIENT_VALUE
                ),
                n_fixed_kg_ha=_rate_kg_ha(n_fix_per_month),
                n_removed_kg_ha=_rate_kg_ha(n_rem_per_harvest * h_count),
                p_removed_kg_ha=_rate_kg_ha(p_rem_per_harvest * h_count),
                k_removed_kg_ha=_rate_kg_ha(k_rem_per_harvest * h_count),
            )

        return monthly_nutrients


class ScenarioNutrientStep(NutrientBalanceStep):
    def apply(
        self,
        candidate: SimulationCandidate,
        context: SolverContext,
    ) -> list[SimulationCandidate]:
        curr_cand, has_missing_data = self._check_removal_rates(
            candidate, context, abort_on_fatal=False
        )

        applied_fertilizer: AppliedFertilizer | None = None
        scheduled_fert = curr_cand.fertilizer_application
        if scheduled_fert is not None:
            fert_product = next(
                (f for f in context.fertilizers if f.id == scheduled_fert.material_id),
                None,
            )
            if fert_product is None:
                curr_cand = curr_cand.with_risk(
                    Note(
                        NoteCategory.DATA_MISSING,
                        NoteImportance.RISK,
                        f"The planned fertilizer material '{scheduled_fert.material_id}' was not found in the catalog",
                    ),
                    fatal_if=False,
                )
            elif float(scheduled_fert.rate_kg_ha.real) > 0.0:
                applied_fertilizer = AppliedFertilizer(
                    product=fert_product,
                    rate=scheduled_fert.rate_kg_ha,
                    target_percentage=scheduled_fert.target_percentage,
                )

        if not curr_cand.stage_offsets:
            return [replace(curr_cand, fertilizer=applied_fertilizer)]

        (
            n_removal,
            p_removal,
            k_removal,
            n_deficit,
            p_deficit,
            k_deficit,
            max_n_fixation,
            max_n_fix_fraction,
        ) = self._calculate_base_requirements(curr_cand)

        raw_texture_baseline = _TEXTURE_BASE_YIELD.get(
            curr_cand.field.soil.texture, DEFAULT_BASE_YIELD
        )
        fert_product = applied_fertilizer.product if applied_fertilizer is not None else None
        is_applied = applied_fertilizer is not None and float(applied_fertilizer.rate.real) > 0.0

        curr_cand = self._apply_fertilizer_notes(
            candidate=curr_cand,
            fert_product=fert_product if is_applied else None,
            n_deficit=n_deficit,
            p_deficit=p_deficit,
            k_deficit=k_deficit,
        )

        total_n_app = ZERO_NUTRIENT_VALUE
        total_p_app = ZERO_NUTRIENT_VALUE
        total_k_app = ZERO_NUTRIENT_VALUE
        if applied_fertilizer is not None and fert_product is not None and is_applied:
            rate_kg_ha = float(applied_fertilizer.rate.real)
            total_n_app = rate_kg_ha * fert_product.n_percent.as_fraction
            total_p_app = rate_kg_ha * fert_product.p_percent.as_fraction
            total_k_app = rate_kg_ha * fert_product.k_percent.as_fraction

        level_pct = (
            round(scheduled_fert.target_percentage.real)
            if scheduled_fert is not None and scheduled_fert.target_percentage is not None
            else None
        )

        curr_cand, fert_yield_mult, actual_n_fixation = self._compute_nutrition_response(
            candidate=curr_cand,
            context=context,
            fert_product=fert_product if is_applied else None,
            total_n_app=total_n_app,
            total_p_app=total_p_app,
            total_k_app=total_k_app,
            n_removal=n_removal,
            p_removal=p_removal,
            k_removal=k_removal,
            p_deficit=p_deficit,
            k_deficit=k_deficit,
            max_n_fixation=max_n_fixation,
            max_n_fix_fraction=max_n_fix_fraction,
            raw_texture_baseline=raw_texture_baseline,
            is_unfertilized=not is_applied,
            level_pct=level_pct,
            abort_on_fatal=False,
        )

        variant = self._finalize_nutrition_candidate(
            candidate=curr_cand,
            fert_yield_mult=fert_yield_mult,
            actual_n_fixation=actual_n_fixation,
            applied_fertilizer=applied_fertilizer if is_applied else None,
            total_n_app=total_n_app,
            total_p_app=total_p_app,
            total_k_app=total_k_app,
            n_removal=n_removal,
            p_removal=p_removal,
            k_removal=k_removal,
            has_missing_data=has_missing_data,
        )
        return [variant]
