from __future__ import annotations

import logging
from dataclasses import dataclass
from functools import partial
from typing import Protocol, TypeAlias

from sqlalchemy import Engine, text

from app.infrastructure.caching import Cache
from app.models.planning import RotationIntervalsMap
from app.models.types import CropId, RotationMonths

logger = logging.getLogger(__name__)


class CropRotationRuleRepository(Protocol):
    async def get_intervals(self) -> RotationIntervalsMap: ...


@dataclass(frozen=True)
class CropRotationRuleRepositoryCacheGetIntervalsKey: ...


CropRotationRuleRepositoryCacheGetIntervals: TypeAlias = Cache[
    CropRotationRuleRepositoryCacheGetIntervalsKey, RotationIntervalsMap
]


class SqlCropRotationRuleRepository(CropRotationRuleRepository):
    def __init__(self, engine: Engine, cache: CropRotationRuleRepositoryCacheGetIntervals) -> None:
        self._engine = engine
        self._cache = cache

    async def _get_intervals(self) -> RotationIntervalsMap:
        query = text("""
                     SELECT crop_id, predecessor_crop_id, min_months_gap
                     FROM crop_rotation_rules;
                     """)

        intervals: RotationIntervalsMap = {}

        with self._engine.connect() as conn:
            result = conn.execute(query)
            for row in result:
                crop_id = CropId(str(row.crop_id))
                predecessor_id = CropId(str(row.predecessor_crop_id))
                gap_months = RotationMonths(int(row.min_months_gap))

                if crop_id not in intervals:
                    intervals[crop_id] = {}
                intervals[crop_id][predecessor_id] = gap_months

        return intervals

    async def get_intervals(self) -> RotationIntervalsMap:
        result = await self._cache.using(
            CropRotationRuleRepositoryCacheGetIntervalsKey(), partial(self._get_intervals)
        )
        return result if result is not None else {}
