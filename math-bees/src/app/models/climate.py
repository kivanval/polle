from __future__ import annotations

from collections.abc import Iterator
from dataclasses import dataclass, field
from datetime import date

from app.models.fao56 import estimate_daily_et0
from app.models.types import Celsius, PrecipitationMm


@dataclass(frozen=True, slots=True)
class DailyClimate:
    date: date
    temp_min_c: Celsius
    temp_max_c: Celsius
    temp_mean_c: Celsius
    precipitation_mm: PrecipitationMm
    et0_mm: float = field(init=False)

    def __post_init__(self):
        doy = self.date.timetuple().tm_yday
        et0 = estimate_daily_et0(
            t_min=float(self.temp_min_c),
            t_max=float(self.temp_max_c),
            t_mean=float(self.temp_mean_c),
            day_of_year=doy,
        )
        object.__setattr__(self, "et0_mm", et0)


@dataclass(frozen=True)
class ClimateTimeline:
    records: dict[date, DailyClimate] = field(default_factory=dict)

    @classmethod
    def from_list(cls, daily_records: list[DailyClimate]) -> ClimateTimeline:
        return cls(records={record.date: record for record in daily_records})

    def get(self, day: date) -> DailyClimate | None:
        return self.records.get(day)

    def __getitem__(self, day: date) -> DailyClimate:
        return self.records[day]

    def __contains__(self, day: date) -> bool:
        return day in self.records

    def __len__(self) -> int:
        return len(self.records)

    def __iter__(self) -> Iterator[date]:
        return iter(self.records)
