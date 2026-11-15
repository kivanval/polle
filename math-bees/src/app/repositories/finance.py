from __future__ import annotations

import logging
from dataclasses import dataclass
from functools import partial
from typing import Any, Protocol

import cashews

from app.autogen.clients.finance.api.default import (
    get_crop_profile_opex_v2,
    get_material_prices_v2,
)
from app.autogen.clients.finance.client import AuthenticatedClient
from app.autogen.clients.finance.models.crop_profile_opex_request_v2 import (
    CropProfileOpexRequestV2,
)
from app.autogen.clients.finance.models.material_pricing_request_v2 import (
    MaterialPricingRequestV2,
)
from app.autogen.clients.finance.types import Unset
from app.config import CachingConfig
from app.infrastructure.caching import Cache
from app.models.types import AnnualRates, AreaHa, CropProfileId, MaterialId, Rate, UsdCents, Year

logger = logging.getLogger(__name__)


class FinanceRepository(Protocol):
    async def get_material_prices(
        self,
        material_ids: list[MaterialId],
        start_year: int | Year,
        end_year: int | Year,
    ) -> dict[MaterialId, AnnualRates]: ...

    async def get_crop_profile_opexs(
        self,
        profile_ids: list[CropProfileId],
        start_year: int | Year,
        end_year: int | Year,
    ) -> dict[CropProfileId, AnnualRates]: ...


def _extract_field(source: Any, key: str) -> Any:
    val = source.get(key) if isinstance(source, dict) else getattr(source, key, None)
    return None if isinstance(val, Unset) else val


def _extract_dict_properties(raw_obj: Any) -> dict[str, Any]:
    if raw_obj is None:
        return {}
    if isinstance(raw_obj, dict):
        return raw_obj
    if hasattr(raw_obj, "additional_properties") and isinstance(
        raw_obj.additional_properties,
        dict,
    ):
        return raw_obj.additional_properties
    if hasattr(raw_obj, "to_dict"):
        data = raw_obj.to_dict()
        if isinstance(data, dict):
            return data
    return {}


def _map_dto_to_annual_rates(rates_raw: Any) -> AnnualRates:
    rates: AnnualRates = {}
    if not rates_raw:
        return rates

    for item in rates_raw:
        raw_year = _extract_field(item, "year")
        raw_cents = _extract_field(item, "price_cents")
        if raw_year is None or raw_cents is None:
            continue

        year = Year(int(raw_year))
        rate: Rate[UsdCents, AreaHa] = Rate(real=int(raw_cents), target_type=UsdCents)
        rates[year] = rate

    return rates


def _map_dto_to_material_prices(dto: Any) -> dict[MaterialId, AnnualRates]:
    raw_material_prices = _extract_field(dto, "material_prices")
    prices_map = _extract_dict_properties(raw_material_prices)

    result: dict[MaterialId, AnnualRates] = {}
    for mat_id_str, rates_raw in prices_map.items():
        mat_id = MaterialId(str(mat_id_str))
        result[mat_id] = _map_dto_to_annual_rates(rates_raw)

    return result


def _map_dto_to_crop_profile_opexs(dto: Any) -> dict[CropProfileId, AnnualRates]:
    raw_opex = _extract_field(dto, "profile_base_opex")
    opex_map = _extract_dict_properties(raw_opex)

    result: dict[CropProfileId, AnnualRates] = {}
    for profile_id_str, rates_raw in opex_map.items():
        profile_id = CropProfileId(str(profile_id_str))
        result[profile_id] = _map_dto_to_annual_rates(rates_raw)

    return result


@dataclass(frozen=True)
class FinanceRepositoryCacheGetMaterialPricesKey:
    material_ids: list[MaterialId]
    start_year: int
    end_year: int


class FinanceRepositoryCacheGetMaterialPrices(
    Cache[FinanceRepositoryCacheGetMaterialPricesKey, dict[MaterialId, AnnualRates]]
):
    def __init__(self, cache: cashews.Cache, config: CachingConfig):
        super().__init__(cache, config)
        self._ttl = config.finance_repository.ttl


@dataclass(frozen=True)
class FinanceRepositoryCacheGetCropProfileOpexsKey:
    profile_ids: list[CropProfileId]
    start_year: int
    end_year: int


class FinanceRepositoryCacheGetCropProfileOpexs(
    Cache[FinanceRepositoryCacheGetCropProfileOpexsKey, dict[CropProfileId, AnnualRates]]
):
    def __init__(self, cache: cashews.Cache, config: CachingConfig):
        super().__init__(cache, config)
        self._ttl = config.finance_repository.ttl


class HttpFinanceRepository(FinanceRepository):
    def __init__(
        self,
        client: AuthenticatedClient,
        material_prices_cache: FinanceRepositoryCacheGetMaterialPrices,
        crop_profile_opexs_cache: FinanceRepositoryCacheGetCropProfileOpexs,
    ) -> None:
        self._client = client
        self._material_prices_cache = material_prices_cache
        self._crop_profile_opexs_cache = crop_profile_opexs_cache

    async def _get_material_prices_from_http(
        self,
        material_ids: list[MaterialId],
        start_year: int | Year,
        end_year: int | Year,
    ) -> dict[MaterialId, AnnualRates]:
        if not material_ids:
            return {}

        start_int = start_year.real if isinstance(start_year, Year) else int(start_year)
        end_int = end_year.real if isinstance(end_year, Year) else int(end_year)

        request_body = MaterialPricingRequestV2(
            start_year=start_int,
            end_year=end_int,
            ids=[str(mid) for mid in material_ids],
        )

        response = await get_material_prices_v2.asyncio_detailed(
            client=self._client,
            body=request_body,
        )

        if response.status_code != 200 or response.parsed is None:
            msg = f"Finance API (materials) error with status {response.status_code}"
            raise RuntimeError(msg)

        return _map_dto_to_material_prices(response.parsed)

    async def get_material_prices(
        self,
        material_ids: list[MaterialId],
        start_year: int | Year,
        end_year: int | Year,
    ) -> dict[MaterialId, AnnualRates]:
        start_int = start_year.real if isinstance(start_year, Year) else int(start_year)
        end_int = end_year.real if isinstance(end_year, Year) else int(end_year)

        result = await self._material_prices_cache.using(
            FinanceRepositoryCacheGetMaterialPricesKey(
                material_ids,
                start_int,
                end_int,
            ),
            partial(self._get_material_prices_from_http, material_ids, start_year, end_year),
        )
        return result if result is not None else {}

    async def _get_crop_profile_opexs_from_http(
        self,
        profile_ids: list[CropProfileId],
        start_year: int | Year,
        end_year: int | Year,
    ) -> dict[CropProfileId, AnnualRates]:
        if not profile_ids:
            return {}

        start_int = start_year.real if isinstance(start_year, Year) else int(start_year)
        end_int = end_year.real if isinstance(end_year, Year) else int(end_year)

        request_body = CropProfileOpexRequestV2(
            start_year=start_int,
            end_year=end_int,
            ids=[str(pid) for pid in profile_ids],
        )

        response = await get_crop_profile_opex_v2.asyncio_detailed(
            client=self._client,
            body=request_body,
        )

        if response.status_code != 200 or response.parsed is None:
            msg = f"Finance API (crop-profiles opex) error with status {response.status_code}"
            raise RuntimeError(msg)

        return _map_dto_to_crop_profile_opexs(response.parsed)

    async def get_crop_profile_opexs(
        self,
        profile_ids: list[CropProfileId],
        start_year: int | Year,
        end_year: int | Year,
    ) -> dict[CropProfileId, AnnualRates]:
        start_int = start_year.real if isinstance(start_year, Year) else int(start_year)
        end_int = end_year.real if isinstance(end_year, Year) else int(end_year)

        result = await self._crop_profile_opexs_cache.using(
            FinanceRepositoryCacheGetCropProfileOpexsKey(
                profile_ids,
                start_int,
                end_int,
            ),
            partial(self._get_crop_profile_opexs_from_http, profile_ids, start_year, end_year),
        )
        return result if result is not None else {}
