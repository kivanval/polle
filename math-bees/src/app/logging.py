from enum import StrEnum, auto


class LoggerTags(StrEnum):
    SIMULATOR_STEP = auto()
    DEATH_REASONS = auto()

    FROM_CACHE = auto()

    def __str__(self):
        return str(f"[{self.value}]")
