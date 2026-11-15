from __future__ import annotations

import logging
from collections.abc import AsyncIterator, Iterator
from pathlib import Path
from typing import Any

import httpx
from cashews import Cache as CashewCache
from dishka import Provider, Scope, provide
from dotenv import load_dotenv
from sqlalchemy import Engine
from taskiq_redis import ListQueueBroker

from app.api.tasks import OptimizeFieldsCache
from app.autogen.clients.climate.client import AuthenticatedClient as ClimateClient
from app.autogen.clients.field_topology.client import AuthenticatedClient as TopologyClient
from app.autogen.clients.finance.client import AuthenticatedClient as FinanceClient
from app.config import (
    AppConfig,
    BrokerConfig,
    CachingConfig,
    DatabaseConfig,
    ExternalServicesConfig,
    LoggingConfig,
    SolverConfig,
)
from app.infrastructure.broker import create_broker
from app.infrastructure.caching import Cache, create_caching
from app.infrastructure.database import create_db_engine
from app.infrastructure.dispatcher import SolverDispatcher
from app.infrastructure.logging import setup_logging
from app.preprocessing.preparer import CanvasPreparer
from app.preprocessing.simulator import AgronomicSimulator
from app.preprocessing.steps.base import SimulationStep
from app.preprocessing.steps.finance import FinanceStep, ScenarioFinanceStep
from app.preprocessing.steps.history_rotation import (
    HistoryRotationStep,
    ScenarioHistoryRotationStep,
)
from app.preprocessing.steps.nutrient import NutrientBalanceStep, ScenarioNutrientStep
from app.preprocessing.steps.phenology import PhenologyStep, ScenarioPhenologyStep
from app.preprocessing.steps.soil import ScenarioSoilConstraintStep, SoilConstraintStep
from app.preprocessing.steps.temperature import (
    AbsoluteTemperatureStep,
    ScenarioTemperatureStep,
)
from app.preprocessing.steps.water import ScenarioWaterStep, SoilWaterBucketStep
from app.preprocessing.studio import CroppingStudio
from app.repositories.climate import (
    ClimateRepository,
    HttpClimateRepository,
)
from app.repositories.crop_profiles import (
    CropProfileRepository,
    SqlCropProfileRepository,
)
from app.repositories.crop_rotation_rules import (
    CropRotationRuleRepository,
    SqlCropRotationRuleRepository,
)
from app.repositories.fertilizers import FertilizersRepository, SqlFertilizersRepository
from app.repositories.fields import (
    FieldsRepository,
    HttpFieldsRepository,
)
from app.repositories.finance import (
    FinanceRepository,
    HttpFinanceRepository,
)
from app.solver.base import Component
from app.solver.components.crop_rotation import CropRotationComponent
from app.solver.components.field_occupancy import FieldOccupancyComponent
from app.solver.components.max_profit import MaxProfitComponent
from app.solver.components.nutrient_accumulation import NutrientAccumulationComponent
from app.solver.components.water_accumulation import WaterAccumulationComponent
from app.solver.formulator import ComponentRegistry
from app.solver.pipeline import SolverPipeline

logger = logging.getLogger(__name__)


class InfrastructureProvider(Provider):
    scope = Scope.APP

    @provide
    def get_app_config(self) -> AppConfig:
        load_dotenv()
        config = AppConfig.load(Path("application.conf"))
        setup_logging(config.logging)
        return config

    @provide
    def get_database_config(self, config: AppConfig) -> DatabaseConfig:
        return config.database

    @provide
    def get_logging_config(self, config: AppConfig) -> LoggingConfig:
        return config.logging

    @provide
    def get_caching_config(self, app_config: AppConfig) -> CachingConfig:
        return app_config.caching

    @provide
    def get_solver_config(self, config: AppConfig) -> SolverConfig:
        return config.solver

    @provide
    def get_external_services_config(self, config: AppConfig) -> ExternalServicesConfig:
        return config.external_services

    @provide
    def get_engine(self, db_config: DatabaseConfig) -> Iterator[Engine]:
        engine = create_db_engine(db_config)
        try:
            yield engine
        finally:
            engine.dispose()

    @provide
    def get_broker_config(self, config: AppConfig) -> BrokerConfig:
        return config.broker

    @provide
    def get_broker(self, broker_config: BrokerConfig) -> ListQueueBroker:
        return create_broker(broker_config)

    @provide
    def get_caching(self, caching_config: CachingConfig) -> CashewCache:
        return create_caching(caching_config)

    solver_dispatcher = provide(SolverDispatcher)


class CacheProvider(Provider):
    scope = Scope.APP
    optimize_fields_cache = provide(OptimizeFieldsCache)


class ClientProvider(Provider):
    scope = Scope.APP

    @provide
    async def get_topology_client(
        self,
        config: ExternalServicesConfig,
    ) -> AsyncIterator[TopologyClient]:
        cfg = config.fields_service

        client = TopologyClient(
            base_url=cfg.base_url,
            timeout=httpx.Timeout(cfg.timeout_seconds),
            token=cfg.token,
            prefix=cfg.auth_prefix,
            auth_header_name=cfg.auth_header_name,
        )
        async with client:
            yield client

    @provide
    async def get_climate_client(
        self,
        config: ExternalServicesConfig,
    ) -> AsyncIterator[ClimateClient]:
        cfg = config.climate_service

        client = ClimateClient(
            base_url=cfg.base_url,
            timeout=httpx.Timeout(cfg.timeout_seconds),
            token=cfg.token,
            prefix=cfg.auth_prefix,
            auth_header_name=cfg.auth_header_name,
        )
        async with client:
            yield client

    @provide
    async def get_finance_client(
        self,
        config: ExternalServicesConfig,
    ) -> AsyncIterator[FinanceClient]:
        cfg = config.finance_service

        client = FinanceClient(
            base_url=cfg.base_url,
            timeout=httpx.Timeout(cfg.timeout_seconds),
            token=cfg.token,
            prefix=cfg.auth_prefix,
            auth_header_name=cfg.auth_header_name,
        )
        async with client:
            yield client


class RepositoryProvider(Provider):
    scope = Scope.APP

    @provide
    def get_crop_profile_repo(
        self, engine: Engine, cache: CashewCache, caching_config: CachingConfig
    ) -> CropProfileRepository:
        return SqlCropProfileRepository(
            engine=engine,
            cache_get_all=Cache(cache, caching_config),
            cache_get_by_id=Cache(cache, caching_config),
        )

    @provide
    def get_crop_rotation_rule_repo(
        self, engine: Engine, cache: CashewCache, caching_config: CachingConfig
    ) -> CropRotationRuleRepository:
        return SqlCropRotationRuleRepository(engine=engine, cache=Cache(cache, caching_config))

    @provide
    def get_fertilizers_repo(
        self, engine: Engine, cache: CashewCache, cashing_config: CachingConfig
    ) -> FertilizersRepository:
        return SqlFertilizersRepository(
            engine, Cache(cache, cashing_config), Cache(cache, cashing_config)
        )

    @provide
    def get_fields_repository(
        self, client: TopologyClient, cache: CashewCache, caching_config: CachingConfig
    ) -> FieldsRepository:
        return HttpFieldsRepository(client=client, cache=Cache(cache, caching_config))

    @provide
    def get_climate_repository(
        self, client: ClimateClient, cache: CashewCache, caching_config: CachingConfig
    ) -> ClimateRepository:
        return HttpClimateRepository(client=client, cache=Cache(cache, caching_config))

    @provide
    def get_finance_repository(
        self, client: FinanceClient, cache: CashewCache, caching_config: CachingConfig
    ) -> FinanceRepository:
        return HttpFinanceRepository(
            client=client,
            material_prices_cache=Cache(cache, caching_config),
            crop_profile_opexs_cache=Cache(cache, caching_config),
        )


class PreprocessingProvider(Provider):
    scope = Scope.REQUEST

    canvas_preparer = provide(CanvasPreparer)
    cropping_studio = provide(CroppingStudio)

    history_step = provide(HistoryRotationStep)
    soil_step = provide(SoilConstraintStep)
    phenology_step = provide(PhenologyStep)
    temperature_step = provide(AbsoluteTemperatureStep)
    water_step = provide(SoilWaterBucketStep)
    nutrition_step = provide(NutrientBalanceStep)
    finance_step = provide(FinanceStep)

    scenario_history_step = provide(ScenarioHistoryRotationStep)
    scenario_phenology_step = provide(ScenarioPhenologyStep)
    scenario_soil_step = provide(ScenarioSoilConstraintStep)
    scenario_temperature_step = provide(ScenarioTemperatureStep)
    scenario_water_step = provide(ScenarioWaterStep)
    scenario_nutrition_step = provide(ScenarioNutrientStep)
    scenario_finance_step = provide(ScenarioFinanceStep)

    @provide
    def get_simulator(
        self,
        history_step: HistoryRotationStep,
        phenology_step: PhenologyStep,
        soil_step: SoilConstraintStep,
        temperature_step: AbsoluteTemperatureStep,
        water_step: SoilWaterBucketStep,
        nutrition_step: NutrientBalanceStep,
        finance_step: FinanceStep,
        scenario_history_step: ScenarioHistoryRotationStep,
        scenario_phenology_step: ScenarioPhenologyStep,
        scenario_soil_step: ScenarioSoilConstraintStep,
        scenario_temperature_step: ScenarioTemperatureStep,
        scenario_water_step: ScenarioWaterStep,
        scenario_nutrition_step: ScenarioNutrientStep,
        scenario_finance_step: ScenarioFinanceStep,
    ) -> AgronomicSimulator:
        optimization_steps: list[SimulationStep] = [
            history_step,
            phenology_step,
            soil_step,
            temperature_step,
            water_step,
            nutrition_step,
            finance_step,
        ]
        scenario_steps: list[SimulationStep] = [
            scenario_history_step,
            scenario_phenology_step,
            scenario_soil_step,
            scenario_temperature_step,
            scenario_water_step,
            scenario_nutrition_step,
            scenario_finance_step,
        ]
        return AgronomicSimulator(
            optimization_steps=optimization_steps,
            scenario_steps=scenario_steps,
        )


class SolverProvider(Provider):
    scope = Scope.APP

    field_occupancy = provide(FieldOccupancyComponent)
    crop_rotation = provide(CropRotationComponent)
    max_profit = provide(MaxProfitComponent)
    water_accumulation = provide(WaterAccumulationComponent)
    nutrition_accumulation = provide(NutrientAccumulationComponent)

    @provide
    def get_component_registry(
        self,
        field_occupancy: FieldOccupancyComponent,
        crop_rotation: CropRotationComponent,
        water_accumulation: WaterAccumulationComponent,
        nutrition_accumulation: NutrientAccumulationComponent,
        max_profit: MaxProfitComponent,
    ) -> ComponentRegistry:
        components: list[Component[Any]] = [
            field_occupancy,
            crop_rotation,
            water_accumulation,
            nutrition_accumulation,
            max_profit,
        ]
        return ComponentRegistry(components)

    solver_pipeline = provide(SolverPipeline)


def get_providers() -> list[Provider]:
    return [
        InfrastructureProvider(),
        CacheProvider(),
        ClientProvider(),
        RepositoryProvider(),
        PreprocessingProvider(),
        SolverProvider(),
    ]
