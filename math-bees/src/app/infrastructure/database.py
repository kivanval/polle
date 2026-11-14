from sqlalchemy import Engine, create_engine
from sqlalchemy.engine import URL

from app.config import DatabaseConfig


def create_db_engine(config: DatabaseConfig) -> Engine:
    url = URL.create(
        drivername="postgresql+psycopg",
        username=config.user,
        password=config.password,
        host=config.host,
        port=config.port,
        database=config.name,
        query={"sslmode": config.sslmode},
    )
    return create_engine(
        url,
        pool_size=config.pool_size,
        max_overflow=config.max_overflow,
        pool_pre_ping=config.pool_pre_ping,
        pool_recycle=config.pool_recycle_seconds,
    )
