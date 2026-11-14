from __future__ import annotations

import logging
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

from dishka import AsyncContainer, make_async_container
from dishka.integrations.fastapi import setup_dishka
from fastapi import FastAPI
from taskiq_redis import ListQueueBroker

from app.api.exceptions import domain_value_error_handler, global_exception_handler
from app.api.router import router as solver_router
from app.providers import get_providers

logger = logging.getLogger(__name__)


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncIterator[None]:
    logger.info("Initializing application resources...")
    container = app.state.dishka_container

    broker = await container.get(ListQueueBroker)

    await broker.startup()
    try:
        yield
    finally:
        await broker.shutdown()
        await container.close()
        logger.info("Application shutdown initiated. Resources released.")


def create_app() -> FastAPI:
    app = FastAPI(
        title="Felder Crop Planning API",
        description="API for optimizing crop rotations and planting schedules.",
        version="1.0.0",
        lifespan=lifespan,
    )

    app.add_exception_handler(ValueError, domain_value_error_handler)
    app.add_exception_handler(Exception, global_exception_handler)

    app.include_router(solver_router)

    @app.get("/healthz", tags=["System"], status_code=200)
    async def healthcheck():
        return {"status": "ok"}

    container: AsyncContainer = make_async_container(*get_providers())
    setup_dishka(container, app)

    return app
