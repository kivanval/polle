from __future__ import annotations

from dishka import make_async_container
from dishka.integrations.taskiq import setup_dishka
from dotenv import load_dotenv

from app.config import AppConfig
from app.infrastructure.broker import create_broker
from app.providers import get_providers

load_dotenv()

config = AppConfig.load()
broker = create_broker(config.broker)

container = make_async_container(*get_providers())
setup_dishka(container, broker)
