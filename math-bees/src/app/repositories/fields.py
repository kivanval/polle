from __future__ import annotations

import contextlib
import logging
from dataclasses import dataclass
from datetime import date
from functools import partial
from typing import Any, Protocol

import cashews

from app.autogen.clients.field_topology.api.default import get_field_by_id_and_month
from app.autogen.clients.field_topology.client import AuthenticatedClient
from app.autogen.clients.field_topology.types import Unset
from app.config import CachingConfig
from app.infrastructure.caching import Cache
from app.models.topology import (
    Field,
    HistoricalVariant,
    NutrientPool,
    SoilHydraulics,
    SoilNutrients,
    SoilProperties,
)
from app.models.types import (
    AreaHa,
    CropId,
    DepletionMm,
    FieldId,
    FieldName,
    Fraction,
    Kilogram,
    Meter,
    Rate,
    SoilDrainageClass,
    SoilTexture,
)
from app.models.year_month import YearMonth

logger = logging.getLogger(__name__)


class FieldsRepository(Protocol):
    async def get_by_id(self, field_id: FieldId, as_of: YearMonth) -> Field | None: ...


def _extract_field(source: Any, key: str) -> Any:
    val = source.get(key) if isinstance(source, dict) else getattr(source, key, None)
    return None if isinstance(val, Unset) else val


def _get_val(prop: str, *sources: Any) -> Any:
    for s in sources:
        if s is not None:
            val = _extract_field(s, prop)
            if val is not None:
                return val
    return None


def _parse_nutrient_pool(
    pool_raw: Any,
    element_name: str,
    field_id: FieldId,
) -> NutrientPool:
    if pool_raw is None:
        msg = f"Field '{field_id}' nutrients is missing mandatory pool '{element_name}'"
        raise ValueError(msg)

    raw_init = _extract_field(pool_raw, "initial_kg_ha")

    if raw_init is None:
        msg = f"Field '{field_id}' nutrient pool '{element_name}' must define 'initial_kg_ha'"
        raise ValueError(msg)

    try:
        init_val = float(raw_init)
    except (ValueError, TypeError) as exc:
        msg = (
            f"Field '{field_id}' nutrient pool '{element_name}' has non-numeric value: "
            f"initial={raw_init!r}"
        )
        raise ValueError(msg) from exc

    if init_val < 0:
        msg = (
            f"Field '{field_id}' nutrient pool '{element_name}' initial_kg_ha must be non-negative: "
            f"got {init_val}"
        )
        raise ValueError(msg)

    return NutrientPool(
        initial_kg_ha=Rate(init_val, target_type=Kilogram),
    )


def _parse_soil_nutrients(nutrients_raw: Any, field_id: FieldId) -> SoilNutrients:
    if nutrients_raw is None:
        msg = f"Field '{field_id}' soil is missing mandatory attribute 'nutrients'"
        raise ValueError(msg)

    n_pool = _parse_nutrient_pool(_extract_field(nutrients_raw, "nitrogen"), "nitrogen", field_id)
    p_pool = _parse_nutrient_pool(
        _extract_field(nutrients_raw, "phosphorus"), "phosphorus", field_id
    )
    k_pool = _parse_nutrient_pool(_extract_field(nutrients_raw, "potassium"), "potassium", field_id)

    return SoilNutrients(
        nitrogen=n_pool,
        phosphorus=p_pool,
        potassium=k_pool,
    )


def _map_dto_to_field(dto: Any) -> Field:
    raw_id = _extract_field(dto, "id")
    if raw_id is None or not str(raw_id).strip():
        msg = "Field DTO is missing mandatory attribute 'id'"
        raise ValueError(msg)

    field_id = FieldId(str(raw_id).strip())

    raw_name = _extract_field(dto, "name")
    if raw_name is None or not str(raw_name).strip():
        msg = f"Field '{field_id}' DTO is missing mandatory attribute 'name'"
        raise ValueError(msg)

    raw_area = _extract_field(dto, "area_ha")
    if raw_area is None:
        msg = f"Field '{field_id}' DTO is missing mandatory attribute 'area_ha'"
        raise ValueError(msg)
    try:
        area_float = float(raw_area)
    except (ValueError, TypeError) as exc:
        msg = f"Field '{field_id}' attribute 'area_ha' must be a valid float, got {raw_area!r}"
        raise ValueError(msg) from exc

    if area_float <= 0:
        msg = f"Field '{field_id}' area_ha must be strictly positive, got {area_float} ha"
        raise ValueError(msg)

    raw_initial_depletion = _extract_field(dto, "initial_depletion_mm")

    if raw_initial_depletion is None:
        initial_depletion = None
    else:
        try:
            dep_float = float(raw_initial_depletion)
        except (ValueError, TypeError) as exc:
            msg = f"Field '{field_id}' has invalid initial_depletion_mm: {raw_initial_depletion!r}"
            raise ValueError(msg) from exc

        if dep_float < 0:
            msg = f"Field '{field_id}' initial_depletion_mm must be non-negative, got {dep_float}"
            raise ValueError(msg)

        initial_depletion = DepletionMm(round(dep_float))

    raw_soil = _extract_field(dto, "soil")
    if raw_soil is None:
        msg = f"Field '{field_id}' DTO is missing mandatory attribute 'soil'"
        raise ValueError(msg)

    raw_texture = _get_val("texture", raw_soil, dto)
    if raw_texture is None:
        msg = f"Field '{field_id}' soil is missing mandatory attribute 'texture'"
        raise ValueError(msg)
    try:
        texture = SoilTexture(str(raw_texture))
    except ValueError as exc:
        msg = f"Field '{field_id}' has unrecognized soil texture: '{raw_texture}'"
        raise ValueError(msg) from exc

    raw_drainage = _get_val("drainage", raw_soil, dto)
    if raw_drainage is None:
        msg = f"Field '{field_id}' soil is missing mandatory attribute 'drainage'"
        raise ValueError(msg)
    try:
        drainage = SoilDrainageClass(str(raw_drainage))
    except ValueError as exc:
        msg = f"Field '{field_id}' has unrecognized drainage class: '{raw_drainage}'"
        raise ValueError(msg) from exc

    raw_ph = _get_val("ph", raw_soil, dto)
    if raw_ph is None:
        msg = f"Field '{field_id}' soil is missing mandatory attribute 'ph'"
        raise ValueError(msg)
    try:
        ph = float(raw_ph)
    except (ValueError, TypeError) as exc:
        msg = f"Field '{field_id}' has invalid pH: {raw_ph!r}"
        raise ValueError(msg) from exc
    if not (0.0 <= ph <= 14.0):
        msg = f"Field '{field_id}' pH must be in range [0.0, 14.0], got {ph}"
        raise ValueError(msg)

    raw_salinity = _get_val("salinity_ds_m", raw_soil, dto)
    if raw_salinity is None:
        msg = f"Field '{field_id}' soil is missing mandatory attribute 'salinity_ds_m'"
        raise ValueError(msg)
    try:
        salinity = float(raw_salinity)
    except (ValueError, TypeError) as exc:
        msg = f"Field '{field_id}' has invalid salinity_ds_m: {raw_salinity!r}"
        raise ValueError(msg) from exc
    if salinity < 0:
        msg = f"Field '{field_id}' salinity_ds_m must be non-negative, got {salinity}"
        raise ValueError(msg)

    raw_hydraulics = _extract_field(raw_soil, "hydraulics") or _extract_field(dto, "hydraulics")
    if raw_hydraulics is None:
        msg = f"Field '{field_id}' soil is missing mandatory attribute 'hydraulics'"
        raise ValueError(msg)

    raw_depth = _get_val("max_soil_depth_m", raw_hydraulics, raw_soil, dto)
    if raw_depth is None:
        msg = f"Field '{field_id}' hydraulics is missing mandatory attribute 'max_soil_depth_m'"
        raise ValueError(msg)
    try:
        depth_val = float(raw_depth)
    except (ValueError, TypeError) as exc:
        msg = f"Field '{field_id}' has invalid max_soil_depth_m: {raw_depth!r}"
        raise ValueError(msg) from exc
    if depth_val <= 0:
        msg = f"Field '{field_id}' max_soil_depth_m must be strictly positive, got {depth_val} m"
        raise ValueError(msg)
    max_soil_depth = Meter(depth_val)

    raw_fc = _get_val("field_capacity_vol", raw_hydraulics, raw_soil, dto)
    if raw_fc is None:
        msg = f"Field '{field_id}' hydraulics is missing mandatory attribute 'field_capacity_vol'"
        raise ValueError(msg)
    try:
        fc_val = float(raw_fc)
    except (ValueError, TypeError) as exc:
        msg = f"Field '{field_id}' has invalid field_capacity_vol: {raw_fc!r}"
        raise ValueError(msg) from exc
    if not (0.0 <= fc_val <= 1.0):
        msg = f"Field '{field_id}' field_capacity_vol must be in range [0.0, 1.0], got {fc_val}"
        raise ValueError(msg)
    field_capacity = Fraction(fc_val)

    raw_pwp = _get_val("wilting_point_vol", raw_hydraulics, raw_soil, dto)
    if raw_pwp is None:
        msg = f"Field '{field_id}' hydraulics is missing mandatory attribute 'wilting_point_vol'"
        raise ValueError(msg)
    try:
        pwp_val = float(raw_pwp)
    except (ValueError, TypeError) as exc:
        msg = f"Field '{field_id}' has invalid wilting_point_vol: {raw_pwp!r}"
        raise ValueError(msg) from exc
    if not (0.0 <= pwp_val <= 1.0):
        msg = f"Field '{field_id}' wilting_point_vol must be in range [0.0, 1.0], got {pwp_val}"
        raise ValueError(msg)
    wilting_point = Fraction(pwp_val)

    if field_capacity <= wilting_point:
        msg = (
            f"Field '{field_id}' field_capacity_vol ({field_capacity}) "
            f"must be strictly greater than wilting_point_vol ({wilting_point})"
        )
        raise ValueError(msg)

    hydraulics = SoilHydraulics(
        max_depth_m=max_soil_depth,
        field_capacity_vol=field_capacity,
        wilting_point_vol=wilting_point,
    )

    raw_nutrients = _extract_field(raw_soil, "nutrients") or _extract_field(dto, "nutrients")
    nutrients = _parse_soil_nutrients(raw_nutrients, field_id)

    soil = SoilProperties(
        texture=texture,
        drainage=drainage,
        ph=ph,
        salinity_ds_m=salinity,
        hydraulics=hydraulics,
        initial_depletion_mm=initial_depletion,
        nutrients=nutrients,
    )

    raw_history = _extract_field(dto, "historical_variants")
    history_list = []
    if raw_history:
        for item in raw_history:
            c_id = _extract_field(item, "crop_profile_id") or _extract_field(item, "crop_id")
            p_date_str = _extract_field(item, "plant_date")
            h_date_str = _extract_field(item, "harvest_date")
            if c_id and p_date_str and h_date_str:
                with contextlib.suppress(ValueError):
                    history_list.append(
                        HistoricalVariant(
                            crop_id=CropId(str(c_id)),
                            plant_date=date.fromisoformat(str(p_date_str)),
                            harvest_date=date.fromisoformat(str(h_date_str)),
                        ),
                    )

    return Field(
        id=field_id,
        name=FieldName(str(raw_name).strip()),
        area_ha=AreaHa(area_float),
        soil=soil,
        history=tuple(history_list),
    )


@dataclass(frozen=True)
class FieldsRepositoryCacheGetByIdKey:
    field_id: FieldId
    year_month: YearMonth


class FieldsRepositoryCacheGetById(Cache[FieldsRepositoryCacheGetByIdKey, Field]):
    def __init__(self, cache: cashews.Cache, config: CachingConfig):
        super().__init__(cache, config)
        self._ttl = config.fields_repository.ttl
        self._enabled = config.fields_repository.enabled


class HttpFieldsRepository:
    def __init__(self, client: AuthenticatedClient, cache: FieldsRepositoryCacheGetById) -> None:
        self._client = client
        self._cache = cache

    async def _get_by_id_from_http(self, field_id: FieldId, as_of: YearMonth) -> Field | None:
        response = await get_field_by_id_and_month.asyncio_detailed(
            field_id=str(field_id),
            year_month=str(as_of),
            client=self._client,
        )
        if response.status_code == 404:
            return None
        if response.status_code != 200 or response.parsed is None:
            msg = f"Topology API error with status {response.status_code}"
            raise RuntimeError(msg)
        result = _map_dto_to_field(response.parsed)
        return result

    async def get_by_id(self, field_id: FieldId, as_of: YearMonth) -> Field | None:
        return await self._cache.using(
            FieldsRepositoryCacheGetByIdKey(field_id, as_of),
            partial(self._get_by_id_from_http, field_id, as_of),
        )
