from __future__ import annotations

import asyncio
import logging
from dataclasses import dataclass
from datetime import date
from functools import partial
from typing import Any, Protocol

import cashews

from app.autogen.clients.climate.api.default import get_field_daily_climate
from app.autogen.clients.climate.client import AuthenticatedClient
from app.config import CachingConfig
from app.infrastructure.caching import Cache
from app.models.climate import ClimateTimeline, DailyClimate
from app.models.types import Celsius, FieldId, PrecipitationMm

logger = logging.getLogger(__file__)


class ClimateRepository(Protocol):
    async def get_by_field_id(
        self,
        field_id: FieldId,
        start_date: date,
        end_date: date,
    ) -> ClimateTimeline | None: ...

    async def get_timelines(
        self,
        field_ids: list[FieldId],
        start_date: date,
        end_date: date,
    ) -> dict[FieldId, ClimateTimeline]: ...


def _map_dto_to_climate(dto: Any) -> DailyClimate:
    record_date = date.fromisoformat(dto.date) if isinstance(dto.date, str) else dto.date
    return DailyClimate(
        date=record_date,
        temp_min_c=Celsius(float(dto.t_min)),
        temp_max_c=Celsius(float(dto.t_max)),
        temp_mean_c=Celsius(float(dto.t_mean)),
        precipitation_mm=PrecipitationMm(float(dto.precip)),
    )


@dataclass(frozen=True)
class ClimateRepositoryCacheGetByFieldIdKey:
    field_id: FieldId
    start_date: date
    end_date: date


class ClimateRepositoryCacheGetByFieldId(
    Cache[ClimateRepositoryCacheGetByFieldIdKey, ClimateTimeline]
):
    def __init__(self, cache: cashews.Cache, config: CachingConfig):
        super().__init__(cache, config)
        self._ttl = config.climate_repository.ttl


class HttpClimateRepository:
    def __init__(
        self, client: AuthenticatedClient, cache: ClimateRepositoryCacheGetByFieldId
    ) -> None:
        self._client = client
        self._cache = cache

    async def _get_by_field_id_from_http(
        self,
        field_id: FieldId,
        start_date: date,
        end_date: date,
    ) -> ClimateTimeline | None:
        response = await get_field_daily_climate.asyncio_detailed(
            field_id=str(field_id),
            start_date=start_date,
            end_date=end_date,
            client=self._client,
        )
        if response.status_code == 404:
            return None
        if response.status_code != 200 or response.parsed is None:
            msg = f"Climate API error with status {response.status_code}"
            raise RuntimeError(msg)
        records = [_map_dto_to_climate(item) for item in response.parsed.timeline]
        return ClimateTimeline.from_list(records)

    async def get_by_field_id(
        self,
        field_id: FieldId,
        start_date: date,
        end_date: date,
    ) -> ClimateTimeline | None:
        return await self._cache.using(
            ClimateRepositoryCacheGetByFieldIdKey(field_id, start_date, end_date),
            partial(self._get_by_field_id_from_http, field_id, start_date, end_date),
        )

    async def get_timelines(
        self,
        field_ids: list[FieldId],
        start_date: date,
        end_date: date,
    ) -> dict[FieldId, ClimateTimeline]:
        timelines = await asyncio.gather(
            *(self.get_by_field_id(fid, start_date, end_date) for fid in field_ids),
        )
        return {
            fid: timeline
            for fid, timeline in zip(field_ids, timelines, strict=False)
            if timeline is not None
        }


class InMemoryClimateRepository:
    def __init__(self, timelines: dict[FieldId, ClimateTimeline] | None = None) -> None:
        self._timelines: dict[FieldId, ClimateTimeline] = timelines or {}

    async def get_by_field_id(
        self,
        field_id: FieldId,
        start_date: date,
        end_date: date,
    ) -> ClimateTimeline | None:
        timeline = self._timelines.get(field_id)
        if timeline is None:
            return None
        filtered_records = [
            record for day, record in timeline.records.items() if start_date <= day <= end_date
        ]
        return ClimateTimeline.from_list(filtered_records)

    async def get_timelines(
        self,
        field_ids: list[FieldId],
        start_date: date,
        end_date: date,
    ) -> dict[FieldId, ClimateTimeline]:
        result: dict[FieldId, ClimateTimeline] = {}
        for fid in field_ids:
            timeline = await self.get_by_field_id(fid, start_date, end_date)
            if timeline is not None:
                result[fid] = timeline
        return result

    def set_timeline(self, field_id: FieldId, timeline: ClimateTimeline) -> None:
        self._timelines[field_id] = timeline

    def set_timelines(self, timelines: dict[FieldId, ClimateTimeline]) -> None:
        self._timelines.update(timelines)
