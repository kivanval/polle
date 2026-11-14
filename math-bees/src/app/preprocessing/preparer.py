from __future__ import annotations

import asyncio
import calendar
from datetime import date

from app.models.climate import ClimateTimeline
from app.models.planning import AgronomicField, CroppingCanvas, Horizon
from app.models.topology import Field
from app.models.types import FieldId
from app.preprocessing.studio import CroppingStudio
from app.repositories.climate import ClimateRepository
from app.repositories.fields import FieldsRepository
from app.solver.strategy import SolverStrategy


def _build_agronomic_field(
    requested_id: FieldId,
    field: Field | None,
    climate: ClimateTimeline | None,
) -> AgronomicField:
    if not field:
        msg = f"Topology data not found for field ID: {requested_id}"
        raise ValueError(msg)

    if not climate:
        msg = f"Climate timeline missing for field ID: {requested_id}"
        raise ValueError(msg)

    return AgronomicField(
        field=field,
        climate=climate,
        fallow_water_balance={},
        fallow_nutrient_recovery={},
    )


class CanvasPreparer:
    def __init__(
        self,
        fields_repo: FieldsRepository,
        climate_repo: ClimateRepository,
        studio: CroppingStudio,
    ) -> None:
        self._fields_repo = fields_repo
        self._climate_repo = climate_repo
        self._studio = studio

    async def prepare(
        self,
        field_id: FieldId,
        horizon: Horizon,
        strategy: SolverStrategy,
    ) -> CroppingCanvas:
        start_date = date(horizon.start.year, horizon.start.month, 1)
        end_date = date(
            horizon.end.year,
            horizon.end.month,
            calendar.monthrange(horizon.end.year, horizon.end.month)[1],
        )

        field, climate = await asyncio.gather(
            self._fields_repo.get_by_id(field_id, as_of=horizon.start),
            self._climate_repo.get_by_field_id(
                field_id=field_id,
                start_date=start_date,
                end_date=end_date,
            ),
        )

        planning_field = _build_agronomic_field(
            requested_id=field_id,
            field=field,
            climate=climate,
        )

        return await self._studio.prepare(
            field=planning_field,
            horizon=horizon,
            strategy=strategy,
        )
