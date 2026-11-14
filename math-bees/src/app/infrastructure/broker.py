from __future__ import annotations

import logging

from taskiq_redis import ListQueueBroker, RedisAsyncResultBackend

from app.api.tasks import (
    EVALUATE_SCENARIO_TASK_NAME,
    OPTIMIZE_FIELDS_TASK_NAME,
    evaluate_scenario_task,
    optimize_fields_task,
)
from app.config import BrokerConfig

logger = logging.getLogger(__name__)


def create_broker(
    broker_config: BrokerConfig,
) -> ListQueueBroker:
    result_backend: RedisAsyncResultBackend = RedisAsyncResultBackend(
        redis_url=broker_config.redis_url,
        socket_keepalive=broker_config.socket_keepalive,
    )
    broker = ListQueueBroker(
        url=broker_config.redis_url,
        queue_name=broker_config.channel_name,
        health_check_interval=broker_config.health_check_interval_seconds,
        socket_timeout=None,
        socket_keepalive=broker_config.socket_keepalive,
    ).with_result_backend(result_backend)

    broker.register_task(
        optimize_fields_task,
        task_name=OPTIMIZE_FIELDS_TASK_NAME,
    )
    broker.register_task(
        evaluate_scenario_task,
        task_name=EVALUATE_SCENARIO_TASK_NAME,
    )

    return broker
