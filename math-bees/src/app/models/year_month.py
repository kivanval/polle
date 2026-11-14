from __future__ import annotations

import calendar
from dataclasses import dataclass
from datetime import date, datetime
from typing import overload

from app.models.types import Month, Year


@dataclass(frozen=True, order=True)
class YearMonth:
    year: int
    month: int

    def __post_init__(self) -> None:
        if not (1 <= self.month <= 12):
            msg = f"Month must be between 1 and 12, got {self.month}"
            raise ValueError(msg)

    @property
    def days_in_month(self) -> int:
        return calendar.monthrange(self.year, self.month)[1]

    @property
    def first_day(self) -> date:
        return date(self.year, self.month, 1)

    @property
    def last_day(self) -> date:
        return date(self.year, self.month, self.days_in_month)

    def to_parts(self) -> tuple[Year, Month]:
        return Year(self.year), Month(self.month)

    def to_year(self) -> Year:
        return Year(self.year)

    def to_month(self) -> Month:
        return Month(self.month)

    def __format__(self, format_spec: str) -> str:
        if not format_spec:
            return str(self)
        return date(self.year, self.month, 1).strftime(format_spec)

    def __str__(self) -> str:
        return f"{self.year:04d}-{self.month:02d}"

    def strftime(self, format_spec: str) -> str:
        return date(self.year, self.month, 1).strftime(format_spec)

    @classmethod
    def from_str(cls, value: str) -> YearMonth:
        try:
            year, month = map(int, value.split("-"))
            return cls(year, month)
        except (ValueError, AttributeError) as exc:
            msg = f"Invalid YearMonth format: '{value}'. Expected 'YYYY-MM'."
            raise ValueError(msg) from exc

    @classmethod
    def strptime(cls, date_str: str, format_spec: str) -> YearMonth:
        dt = datetime.strptime(date_str, format_spec)
        return cls(dt.year, dt.month)

    def __add__(self, months: int) -> YearMonth:
        if not isinstance(months, int):
            return NotImplemented
        total = self.year * 12 + (self.month - 1) + months
        new_year, new_month_zero = divmod(total, 12)
        return YearMonth(new_year, new_month_zero + 1)

    @overload
    def __sub__(self, other: YearMonth) -> int: ...

    @overload
    def __sub__(self, other: int) -> YearMonth: ...

    def __sub__(self, other: YearMonth | int) -> YearMonth | int:
        if isinstance(other, int):
            return self + (-other)
        if isinstance(other, YearMonth):
            return (self.year * 12 + self.month) - (other.year * 12 + other.month)
        return NotImplemented
