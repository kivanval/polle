from __future__ import annotations

from dataclasses import dataclass
from enum import StrEnum
from typing import Any, Generic, NewType, Self, TypeAlias, TypeVar, overload

# --- Identifiers ---
FarmId = NewType("FarmId", str)
FieldId = NewType("FieldId", str)
CropId = NewType("CropId", str)
CropProfileId = NewType("CropProfileId", str)
CropStageId = NewType("CropStageId", str)
MaterialId = NewType("MaterialId", str)
VariantId = NewType("VariantId", str)

# --- Names & Labels ---
ScientificName = NewType("ScientificName", str)
CropName = NewType("CropName", str)
FieldName = NewType("FieldName", str)
SystemName = NewType("SystemName", str)
LanguageCode = NewType("LanguageCode", str)


@dataclass(slots=True, frozen=True)
class DeathNote:
    reason: str
    is_static: bool = False


class StageCategory(StrEnum):
    PLANTING = "planting"
    VEGETATIVE = "vegetative"
    FLOWERING = "flowering"
    HARVEST = "harvest"
    DORMANCY = "dormancy"
    REGROWTH = "regrowth"


class SoilTexture(StrEnum):
    SAND = "sand"
    LOAMY_SAND = "loamy_sand"
    SANDY_LOAM = "sandy_loam"
    LOAM = "loam"
    SILT_LOAM = "silt_loam"
    CLAY_LOAM = "clay_loam"
    CLAY = "clay"
    PEAT = "peat"


class SoilDrainageClass(StrEnum):
    WELL_DRAINED = "well_drained"
    MODERATE = "moderate"
    POOR = "poor"


IsPerennial = NewType("IsPerennial", bool)

# ==============================================================================
# Domain Primitives & Generics
# ==============================================================================

T = TypeVar("T", int, float)


@dataclass(frozen=True, slots=True, order=True)
class DomainValue(Generic[T]):
    real: T

    def __add__(self, other: Self) -> Self:
        val: Any = self.real + other.real
        return self.__class__(val)

    def __sub__(self, other: Self) -> Self:
        val: Any = self.real - other.real
        return self.__class__(val)

    def __mul__(self, other: float | Self) -> Self:
        val: Any = self.real * other.real if isinstance(other, DomainValue) else self.real * other
        return self.__class__(val)

    def __rmul__(self, other: float) -> Self:
        val: Any = other * self.real
        return self.__class__(val)

    def __int__(self) -> int:
        val: Any = self.real
        return int(val)

    def __float__(self) -> float:
        val: Any = self.real
        return float(val)

    def __index__(self) -> int:
        return int(self.real)


N = TypeVar("N", bound=DomainValue[Any])
D = TypeVar("D", bound=DomainValue[Any])


@dataclass(frozen=True, slots=True)
class Rate(Generic[N, D]):
    real: float | int
    target_type: type[N]

    def __mul__(self, denominator: D) -> N:
        val: Any = self.real * denominator.real
        if issubclass(self.target_type, UsdCents):
            val = round(val)
        return self.target_type(val)

    def __truediv__(self, denominator: D) -> N:
        val: Any = self.real / denominator.real
        return self.target_type(val)


# --- Float measures ---
class AreaHa(DomainValue[float]):
    pass


class YieldPerHa(DomainValue[float]):
    pass


class Usd(DomainValue[float]):
    pass


class Celsius(DomainValue[float]):
    pass


class GDD(DomainValue[float]):
    pass


class VD(DomainValue[float]):
    pass


class PrecipitationMm(DomainValue[float]):
    pass


class WaterMm(DomainValue[float]):
    pass


class Meter(DomainValue[float]):
    pass


class Fraction(DomainValue[float]):
    pass


class CropCoefficient(DomainValue[float]):
    pass


class YieldResponseFactor(DomainValue[float]):
    pass


class YieldMultiplier(DomainValue[float]):
    pass


class SoilPh(DomainValue[float]):
    pass


class SalinityDsM(DomainValue[float]):
    pass


class Kilogram(DomainValue[float]):
    pass


class YieldUnit(DomainValue[float]):
    pass


class Percent(DomainValue[float]):
    @classmethod
    def from_fraction(cls, fraction: float, *, decimals: int | None = 2) -> Self:
        value = fraction * 100.0
        if decimals is not None:
            value = round(value, decimals)
        return cls(value)

    @property
    def as_fraction(self) -> float:
        return self.real / 100.0


# --- Integer measures ---
class UsdCents(DomainValue[int]):
    pass


class SequenceOrder(DomainValue[int]):
    pass


class GrowingDays(DomainValue[int]):
    pass


class DayOffset(DomainValue[int]):
    pass


class ToleranceDays(DomainValue[int]):
    pass


class DurationMonths(DomainValue[int]):
    pass


class RotationMonths(DomainValue[int]):
    pass


class LifespanYears(DomainValue[int]):
    pass


class Month(DomainValue[int]):
    pass


class DepletionMm(DomainValue[int]):
    pass


@dataclass(frozen=True, slots=True, order=True)
class Year:
    real: int

    def __add__(self, years: int) -> Year:
        return Year(self.real + years)

    @overload
    def __sub__(self, other: Year) -> int: ...

    @overload
    def __sub__(self, other: int) -> Year: ...

    def __sub__(self, other: Year | int) -> int | Year:
        if isinstance(other, int):
            return Year(self.real - other)
        return int(self.real - other.real)


# ==============================================================================
# Domain Entities
# ==============================================================================


@dataclass(frozen=True, slots=True)
class MonthlyWaterDynamics:
    applied_mm: WaterMm
    removed_mm: WaterMm
    net_balance_mm: WaterMm


@dataclass(frozen=True, slots=True)
class MonthlyNutrientDynamics:
    n_applied_kg_ha: Rate[Kilogram, AreaHa]
    n_fixed_kg_ha: Rate[Kilogram, AreaHa]
    n_removed_kg_ha: Rate[Kilogram, AreaHa]

    p_applied_kg_ha: Rate[Kilogram, AreaHa]
    p_removed_kg_ha: Rate[Kilogram, AreaHa]

    k_applied_kg_ha: Rate[Kilogram, AreaHa]
    k_removed_kg_ha: Rate[Kilogram, AreaHa]


@dataclass(frozen=True, slots=True)
class NutrientBalance:
    n_removal_kg_ha: Rate[Kilogram, AreaHa]
    p_removal_kg_ha: Rate[Kilogram, AreaHa]
    k_removal_kg_ha: Rate[Kilogram, AreaHa]
    n_fixation_kg_ha: Rate[Kilogram, AreaHa]

    n_applied_kg_ha: Rate[Kilogram, AreaHa]
    p_applied_kg_ha: Rate[Kilogram, AreaHa]
    k_applied_kg_ha: Rate[Kilogram, AreaHa]

    @property
    def net_n_kg_ha(self) -> Rate[Kilogram, AreaHa]:
        return Rate(
            self.n_applied_kg_ha.real + self.n_fixation_kg_ha.real - self.n_removal_kg_ha.real,
            Kilogram,
        )

    @property
    def net_p_kg_ha(self) -> Rate[Kilogram, AreaHa]:
        return Rate(self.p_applied_kg_ha.real - self.p_removal_kg_ha.real, Kilogram)

    @property
    def net_k_kg_ha(self) -> Rate[Kilogram, AreaHa]:
        return Rate(self.k_applied_kg_ha.real - self.k_removal_kg_ha.real, Kilogram)


# ==============================================================================
# Aliases & Converters
# ==============================================================================

AnnualRates: TypeAlias = dict[Year, Rate[UsdCents, AreaHa]]

CENTS_PER_USD = 100


def cents_to_usd(cents: UsdCents) -> Usd:
    return Usd(round(cents.real / CENTS_PER_USD, 2))


def usd_to_cents(usd: Usd) -> UsdCents:
    return UsdCents(round(usd.real * CENTS_PER_USD))


# --- Risks ---
class NoteCategory(StrEnum):
    DATA_MISSING = "DATA_MISSING"
    AGRONOMIC = "AGRONOMIC"
    SOIL_HEALTH = "SOIL_HEALTH"


class NoteImportance(StrEnum):
    INFO = "INFO"
    WARN = "WARN"
    RISK = "RISK"


@dataclass(frozen=True)
class Note:
    category: NoteCategory
    importance: NoteImportance
    message: str

    def __str__(self) -> str:
        return f"[{self.category.value}] {self.message}"
