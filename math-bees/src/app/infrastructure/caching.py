import logging
from collections.abc import Awaitable, Callable
from typing import Generic, TypeVar

import cashews

from app.config import CachingConfig
from app.logging import LoggerTags

logger = logging.getLogger(__name__)
K = TypeVar("K", contravariant=True)
V = TypeVar("V")


class Cache(Generic[K, V]):
    def __init__(self, cache: cashews.Cache, config: CachingConfig):
        self._cache = cache
        self._config = config
        self._ttl = config.ttl
        self._enabled = config.enabled

    async def get(self, key: K) -> V | None:
        if not self._enabled:
            return None
        result = await self._cache.get(f"{type(key)}_{key!s}")
        if result is not None:
            logger.debug(f"{LoggerTags.FROM_CACHE} Found cached data for %s", key)
        return result

    async def set(self, key: K, value: V) -> None:
        logger.debug(f"{LoggerTags.FROM_CACHE} Setting cache for %s for %s", key, self._ttl)
        await self._cache.set(f"{type(key)}_{key!s}", value, expire=self._ttl)

    async def using(self, key: K, get_value: Callable[[], Awaitable[V | None]]) -> V | None:
        result = await self.get(key)
        if result is None:
            result = await get_value()
            if result is not None:
                await self.set(key, result)
        return result


def create_caching(config: CachingConfig) -> cashews.Cache:
    cache = cashews.Cache()
    cache.setup(config.redis_url, enable=config.enabled, suppress=True)
    return cache
