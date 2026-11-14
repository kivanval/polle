from __future__ import annotations

from dataclasses import dataclass
from functools import partial
from typing import Protocol, TypeAlias

from sqlalchemy import Engine, text

from app.infrastructure.caching import Cache
from app.models.agronomy import Fertilizer, SoilImpact
from app.models.types import MaterialId, Percent


class FertilizersRepository(Protocol):
    async def get_all(self) -> list[Fertilizer]: ...
    async def get_by_id(self, material_id: MaterialId) -> Fertilizer | None: ...


@dataclass(frozen=True)
class FertilizersRepositoryCacheGetAllKey: ...


FertilizersRepositoryCacheGetAll: TypeAlias = Cache[
    FertilizersRepositoryCacheGetAllKey, list[Fertilizer]
]


@dataclass(frozen=True)
class SqlFertilizersRepositoryCacheGetByIdKey:
    id: MaterialId


SqlFertilizersRepositoryCacheGetById: TypeAlias = Cache[
    SqlFertilizersRepositoryCacheGetByIdKey, Fertilizer
]


class SqlFertilizersRepository(FertilizersRepository):
    _BASE_QUERY = """
                  SELECT
                      material_id AS id,
                      n_percent,
                      p_percent,
                      k_percent,
                      soil_impact
                  FROM fertilizers \
                  """

    def __init__(
        self,
        engine: Engine,
        cache_get_all: FertilizersRepositoryCacheGetAll,
        cache_get_by_id: SqlFertilizersRepositoryCacheGetById,
    ) -> None:
        self._engine = engine
        self._cache_get_all = cache_get_all
        self._cache_get_by_id = cache_get_by_id

    async def _get_all_from_db(self) -> list[Fertilizer]:
        query = text(f"{self._BASE_QUERY} ORDER BY material_id;")
        with self._engine.connect() as conn:
            result = conn.execute(query)
            return [
                Fertilizer(
                    id=MaterialId(str(row.id)),
                    n_percent=Percent(float(row.n_percent)),
                    p_percent=Percent(float(row.p_percent)),
                    k_percent=Percent(float(row.k_percent)),
                    soil_impact=SoilImpact(str(row.soil_impact)),
                )
                for row in result
            ]

    async def get_all(self) -> list[Fertilizer]:
        result = await self._cache_get_all.using(
            FertilizersRepositoryCacheGetAllKey(), self._get_all_from_db
        )
        return result if result is not None else []

    async def _get_by_id_from_db(self, material_id: MaterialId) -> Fertilizer | None:
        try:
            db_id = int(material_id)
        except ValueError:
            return None

        query = text(f"{self._BASE_QUERY} WHERE material_id = :material_id;")
        with self._engine.connect() as conn:
            row = conn.execute(query, {"material_id": db_id}).first()
            if not row:
                return None
            return Fertilizer(
                id=MaterialId(str(row.id)),
                n_percent=Percent(float(row.n_percent)),
                p_percent=Percent(float(row.p_percent)),
                k_percent=Percent(float(row.k_percent)),
                soil_impact=SoilImpact(str(row.soil_impact)),
            )

    async def get_by_id(self, material_id: MaterialId) -> Fertilizer | None:
        return await self._cache_get_by_id.using(
            SqlFertilizersRepositoryCacheGetByIdKey(material_id),
            partial(self._get_by_id_from_db, material_id),
        )
