from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

from pyhocon import ConfigFactory, ConfigTree


@dataclass(frozen=True)
class DatabaseConfig:
    host: str
    port: int
    user: str
    password: str
    name: str
    sslmode: str
    pool_size: int
    max_overflow: int
    pool_pre_ping: bool
    pool_recycle_seconds: int

    @classmethod
    def from_hocon(cls, tree: ConfigTree) -> DatabaseConfig:
        cfg = tree.get_config("app.database")
        return cls(
            host=cfg.get_string("host"),
            port=cfg.get_int("port"),
            user=cfg.get_string("user"),
            password=cfg.get_string("password"),
            name=cfg.get_string("name"),
            sslmode=cfg.get_string("sslmode", "require"),
            pool_size=cfg.get_int("pool-size"),
            max_overflow=cfg.get_int("max-overflow"),
            pool_pre_ping=cfg.get_bool("pool-pre-ping"),
            pool_recycle_seconds=cfg.get_int("pool-recycle-seconds"),
        )


@dataclass(frozen=True)
class BrokerConfig:
    redis_url: str
    channel_name: str
    health_check_interval_seconds: int
    socket_keepalive: bool

    @classmethod
    def from_hocon(cls, tree: ConfigTree) -> BrokerConfig:
        cfg = tree.get_config("app.broker")
        return cls(
            redis_url=cfg.get_string("redis-url"),
            channel_name=cfg.get_string("channel-name"),
            health_check_interval_seconds=cfg.get_int("health-check-interval-seconds"),
            socket_keepalive=cfg.get_bool("socket-keepalive"),
        )


@dataclass(frozen=True)
class LoggingConfig:
    level: str
    format: str

    @classmethod
    def from_hocon(cls, tree: ConfigTree) -> LoggingConfig:
        cfg = tree.get_config("app.logging")
        return cls(
            level=cfg.get_string("level", "INFO"),
            format=cfg.get_string("format"),
        )


@dataclass(frozen=True)
class SolverConfig:
    num_workers: int
    time_limit_seconds: float
    random_seed: int
    log_search_progress: bool
    relative_gap_limit: float

    @classmethod
    def from_hocon(cls, tree: ConfigTree) -> SolverConfig:
        cfg = tree.get_config("app.solver")
        return cls(
            num_workers=cfg.get_int("num-workers"),
            time_limit_seconds=cfg.get_float("time-limit-seconds"),
            random_seed=cfg.get_int("random-seed"),
            log_search_progress=cfg.get_bool("log-search-progress"),
            relative_gap_limit=cfg.get_float("relative-gap-limit"),
        )


@dataclass(frozen=True)
class ServiceClientConfig:
    base_url: str
    timeout_seconds: float

    @classmethod
    def from_hocon(cls, tree: ConfigTree, path: str) -> ServiceClientConfig:
        cfg = tree.get_config(path)
        return cls(
            base_url=cfg.get_string("base-url"),
            timeout_seconds=cfg.get_float("timeout-seconds"),
        )


@dataclass(frozen=True)
class AuthorizedServiceClientConfig:
    base_url: str
    timeout_seconds: float
    token: str
    auth_header_name: str
    auth_prefix: str

    @classmethod
    def from_hocon(cls, tree: ConfigTree, path: str) -> AuthorizedServiceClientConfig:
        cfg = tree.get_config(path)
        return cls(
            base_url=cfg.get_string("base-url"),
            timeout_seconds=cfg.get_float("timeout-seconds"),
            token=cfg.get_string("token"),
            auth_header_name=cfg.get_string("auth-header-name"),
            auth_prefix=cfg.get_string("auth-prefix", ""),
        )


@dataclass(frozen=True)
class ExternalServicesConfig:
    fields_service: AuthorizedServiceClientConfig
    climate_service: AuthorizedServiceClientConfig
    finance_service: AuthorizedServiceClientConfig

    @classmethod
    def from_hocon(cls, tree: ConfigTree) -> ExternalServicesConfig:
        return cls(
            fields_service=AuthorizedServiceClientConfig.from_hocon(tree, "app.services.fields"),
            climate_service=AuthorizedServiceClientConfig.from_hocon(tree, "app.services.climate"),
            finance_service=AuthorizedServiceClientConfig.from_hocon(tree, "app.services.finance"),
        )


@dataclass(frozen=True)
class CachingConfig:
    ttl: str
    redis_url: str
    enabled: bool

    @classmethod
    def from_hocon(cls, tree: ConfigTree) -> CachingConfig:
        cfg = tree.get_config("app.caching")
        return CachingConfig(
            ttl=cfg.get_string("ttl"),
            redis_url=cfg.get_string("redis-url"),
            enabled=cfg.get_bool("enabled"),
        )


@dataclass(frozen=True)
class AppConfig:
    database: DatabaseConfig
    caching: CachingConfig
    logging: LoggingConfig
    solver: SolverConfig
    broker: BrokerConfig
    external_services: ExternalServicesConfig

    @classmethod
    def load(cls, path: Path = Path("application.conf")) -> AppConfig:
        tree = ConfigFactory.parse_file(str(path))
        return cls(
            database=DatabaseConfig.from_hocon(tree),
            caching=CachingConfig.from_hocon(tree),
            logging=LoggingConfig.from_hocon(tree),
            solver=SolverConfig.from_hocon(tree),
            broker=BrokerConfig.from_hocon(tree),
            external_services=ExternalServicesConfig.from_hocon(tree),
        )
