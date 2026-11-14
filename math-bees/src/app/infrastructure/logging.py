import logging

from app.config import LoggingConfig


def setup_logging(config: LoggingConfig) -> None:
    level = getattr(logging, config.level.upper(), logging.INFO)
    logging.basicConfig(level=level, format=config.format)
