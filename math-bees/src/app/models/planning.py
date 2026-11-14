from __future__ import annotations

from dataclasses import dataclass
from datetime import date
from typing import TypeAlias

from app.models.agronomy import AppliedFertilizer, Crop, CropProfile, Fertilizer
from app.models.climate import ClimateTimeline
from app.models.topology import Field
from app.models.types import (
    AnnualRates,
    AreaHa,
    CropId,
    CropName,
    CropProfileId,
    DepletionMm,
    DurationMonths,
    FieldId,
    Kilogram,
    MaterialId,
    MonthlyNutrientDynamics,
    MonthlyWaterDynamics,
    Note,
    Rate,
    RotationMonths,
    SystemName,
    UsdCents,
    VariantId,
    WaterMm,
    YieldMultiplier,
)
from app.models.year_month import YearMonth
from app.solver.settings import ReplenishmentSetting, RiskSetting


@dataclass(frozen=True)
class Horizon:
    start: YearMonth
    end: YearMonth

    def __post_init__(self) -> None:
        if self.start > self.end:
            msg = f"Horizon start ({self.start}) cannot be after end ({self.end})"
            raise ValueError(msg)

    @property
    def months(self) -> list[YearMonth]:
        total = (self.end - self.start) + 1
        return [self.start + i for i in range(total)]

    def __len__(self) -> int:
        return (self.end - self.start) + 1


@dataclass(frozen=True, slots=True)
class AgronomicField:
    field: Field
    climate: ClimateTimeline
    fallow_water_balance: dict[YearMonth, float]
    fallow_nutrient_recovery: dict[tuple[str, YearMonth], float]

    @property
    def id(self) -> FieldId:
        return self.field.id

    @property
    def name(self) -> str:
        return self.field.name

    @property
    def area_ha(self) -> AreaHa:
        return self.field.area_ha


RotationIntervalsMap: TypeAlias = dict[CropId, dict[CropId, RotationMonths]]


@dataclass
class SolverContext:
    crop_profiles: list[CropProfile]
    horizon: Horizon
    field: AgronomicField

    rotation_intervals: RotationIntervalsMap
    fertilizers: list[Fertilizer]

    material_prices: dict[MaterialId, AnnualRates]
    crop_profile_opexs: dict[CropProfileId, AnnualRates]

    replenishment: ReplenishmentSetting
    risk: RiskSetting

    @property
    def months(self) -> list[YearMonth]:
        return self.horizon.months


@dataclass(frozen=True)
class VariantProfile:
    id: CropProfileId
    crop: Crop
    system_name: SystemName

    max_planting_depletion_mm: DepletionMm | None
    min_planting_depletion_mm: DepletionMm | None

    min_planting_n_kg_ha: Rate[Kilogram, AreaHa] | None
    max_planting_n_kg_ha: Rate[Kilogram, AreaHa] | None
    min_planting_p_kg_ha: Rate[Kilogram, AreaHa] | None
    max_planting_p_kg_ha: Rate[Kilogram, AreaHa] | None
    min_planting_k_kg_ha: Rate[Kilogram, AreaHa] | None
    max_planting_k_kg_ha: Rate[Kilogram, AreaHa] | None

    planting_material_id: MaterialId
    product_id: MaterialId | None

    def get_name(self) -> CropName:
        return CropName(f"{self.crop.scientific_name} ({self.system_name})")


@dataclass(frozen=True, slots=True)
class ScheduledFertilizerApplication:
    material_id: MaterialId
    rate_kg_ha: Rate[Kilogram, AreaHa]


@dataclass(frozen=True, slots=True)
class ScheduledIrrigationApplication:
    material_id: MaterialId
    amount_mm: WaterMm


@dataclass(frozen=True, slots=True)
class CropScheduleItem:
    profile_id: CropProfileId
    plant_date: date
    clearance_date: date | None
    fertilizer_application: ScheduledFertilizerApplication | None
    irrigation_application: ScheduledIrrigationApplication | None


@dataclass(frozen=True, slots=True)
class PlannedCrop:
    profile: CropProfile
    plant_date: date
    clearance_date: date | None
    fertilizer_application: ScheduledFertilizerApplication | None
    irrigation_application: ScheduledIrrigationApplication | None


@dataclass(frozen=True, slots=True)
class PlantingVariant:
    field_id: FieldId
    profile: VariantProfile
    plant_date: date
    clearance_date: date
    harvest_events: tuple[date, ...]

    gross_revenue_cents: UsdCents
    base_opex_cents: UsdCents
    planting_cost_cents: UsdCents
    water_cost_cents: UsdCents
    fertilizer_cost_cents: UsdCents

    monthly_water: dict[YearMonth, MonthlyWaterDynamics]
    monthly_nutrients: dict[YearMonth, MonthlyNutrientDynamics]

    fertilizer: AppliedFertilizer | None

    yield_multipliers: tuple[YieldMultiplier, ...]
    notes: tuple[Note, ...]

    @property
    def expenses_cents(self) -> UsdCents:
        return UsdCents(
            self.base_opex_cents.real
            + self.planting_cost_cents.real
            + self.water_cost_cents.real
            + self.fertilizer_cost_cents.real
        )

    @property
    def expected_revenue(self) -> UsdCents:
        return UsdCents(int(self.gross_revenue_cents.real - self.expenses_cents.real))

    @property
    def plant_month(self) -> YearMonth:
        return YearMonth(self.plant_date.year, self.plant_date.month)

    @property
    def clearance_month(self) -> YearMonth:
        return YearMonth(self.clearance_date.year, self.clearance_date.month)

    @property
    def id(self) -> VariantId:
        return VariantId(f"{self.profile.id}_{self.plant_date}_{self.clearance_date}")

    @property
    def duration_months(self) -> DurationMonths:
        return DurationMonths((self.clearance_month - self.plant_month) + 1)

    @property
    def occupied_months(self) -> list[YearMonth]:
        return [self.plant_month + i for i in range(self.duration_months)]

    def get_name(self) -> CropName:
        return self.profile.get_name()


@dataclass
class CroppingCanvas:
    horizon: Horizon
    field: AgronomicField
    variants: list[PlantingVariant]
    rotation_intervals: RotationIntervalsMap
    material_prices: dict[MaterialId, AnnualRates]
    risk: RiskSetting

    @property
    def months(self) -> list[YearMonth]:
        return self.horizon.months

    @property
    def crop_profiles(self) -> list[VariantProfile]:
        unique_profiles: dict[CropProfileId, VariantProfile] = {}
        for v in self.variants:
            if v.profile.id not in unique_profiles:
                unique_profiles[v.profile.id] = v.profile
        return list(unique_profiles.values())
