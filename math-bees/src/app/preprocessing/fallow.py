from __future__ import annotations

from collections import defaultdict
from dataclasses import replace
from datetime import date, timedelta

from app.models.fao56 import KC_BARE_SOIL
from app.models.planning import AgronomicField
from app.models.soil_recovery import calculate_fallow_nutrient_recovery
from app.models.year_month import YearMonth


def simulate_fallow_dynamics(
    agronomic_field: AgronomicField,
    horizon_start: date,
    total_horizon_days: int,
) -> AgronomicField:
    soil_texture = agronomic_field.field.soil.texture

    fallow_water_balance: dict[YearMonth, float] = defaultdict(float)
    fallow_nutrient_recovery: dict[tuple[str, YearMonth], float] = defaultdict(float)
    records = agronomic_field.climate.records

    for day_offset in range(total_horizon_days):
        current_date = horizon_start + timedelta(days=day_offset)
        record = records.get(current_date)
        if not record:
            continue

        ym = YearMonth(current_date.year, current_date.month)

        fallow_water_balance[ym] += float(
            (KC_BARE_SOIL * record.et0_mm) - float(record.precipitation_mm)
        )

        daily_n, daily_p, daily_k = calculate_fallow_nutrient_recovery(
            t_mean_c=float(record.temp_mean_c), texture=soil_texture
        )
        fallow_nutrient_recovery[("n", ym)] += daily_n
        fallow_nutrient_recovery[("p", ym)] += daily_p
        fallow_nutrient_recovery[("k", ym)] += daily_k

    return replace(
        agronomic_field,
        fallow_water_balance=dict(fallow_water_balance),
        fallow_nutrient_recovery=dict(fallow_nutrient_recovery),
    )
