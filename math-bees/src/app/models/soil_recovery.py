from __future__ import annotations

from typing import Final

from app.models.types import SoilTexture

# ---------------------------------------------------------------------------
# Thermal biological limits for soil microbial mineralization (°C)
# ---------------------------------------------------------------------------
BIO_ACTIVITY_BASE_TEMP_C: Final[float] = 4.0
BIO_ACTIVITY_OPTIMUM_TEMP_C: Final[float] = 24.0

# ---------------------------------------------------------------------------
# Base potential daily replenishment rates at optimum 24°C (kg/ha/day)
# ---------------------------------------------------------------------------
BASE_DAILY_RECOVERY_N_KG_HA: Final[float] = 0.12
BASE_DAILY_RECOVERY_P_KG_HA: Final[float] = 0.04
BASE_DAILY_RECOVERY_K_KG_HA: Final[float] = 0.08

# ---------------------------------------------------------------------------
# Cation exchange capacity (CEC) multipliers by official SoilTexture enum
# ---------------------------------------------------------------------------
_TEXTURE_MULTIPLIERS: Final[dict[SoilTexture, tuple[float, float, float]]] = {
    SoilTexture.CLAY: (1.10, 0.85, 1.30),
    SoilTexture.CLAY_LOAM: (1.10, 0.85, 1.30),
    SoilTexture.SAND: (0.75, 1.10, 0.60),
    SoilTexture.LOAMY_SAND: (0.75, 1.10, 0.60),
    SoilTexture.SANDY_LOAM: (0.90, 1.05, 0.85),
    SoilTexture.LOAM: (1.00, 1.00, 1.00),
    SoilTexture.SILT_LOAM: (1.00, 1.00, 1.00),
    SoilTexture.PEAT: (1.30, 0.90, 0.70),
}
_DEFAULT_MULTIPLIER: Final[tuple[float, float, float]] = (1.00, 1.00, 1.00)


def calculate_fallow_nutrient_recovery(
    t_mean_c: float,
    texture: SoilTexture,
) -> tuple[float, float, float]:
    """Calculate daily N, P, K replenishment in root zone during fallow periods.

    Returns:
        tuple[float, float, float]: (daily_n_kg_ha, daily_p_kg_ha, daily_k_kg_ha)

    """
    if t_mean_c <= BIO_ACTIVITY_BASE_TEMP_C:
        temp_factor = 0.0
    elif t_mean_c >= BIO_ACTIVITY_OPTIMUM_TEMP_C:
        temp_factor = 1.0
    else:
        temp_factor = (t_mean_c - BIO_ACTIVITY_BASE_TEMP_C) / (
            BIO_ACTIVITY_OPTIMUM_TEMP_C - BIO_ACTIVITY_BASE_TEMP_C
        )

    mult_n, mult_p, mult_k = _TEXTURE_MULTIPLIERS.get(texture, _DEFAULT_MULTIPLIER)

    daily_n = BASE_DAILY_RECOVERY_N_KG_HA * temp_factor * mult_n
    daily_p = BASE_DAILY_RECOVERY_P_KG_HA * temp_factor * mult_p
    daily_k = BASE_DAILY_RECOVERY_K_KG_HA * temp_factor * mult_k

    return daily_n, daily_p, daily_k
