import numpy as np

# --- Hydrological Constants (FAO-56) ---
WILTING_DEPLETION_RATIO: float = 0.98
KC_BARE_SOIL: float = 0.2

# Hargreaves-Samani Empirical Equation Constants
HARGREAVES_COEFFICIENT: float = 0.0023
HARGREAVES_TEMP_OFFSET_C: float = 17.8
SOLAR_RAD_BASE_MM_PER_DAY: float = 15.0
SOLAR_RAD_AMPLITUDE: float = 0.75
DAYS_PER_YEAR: float = 365.25
SOLSTICE_DAY_OFFSET: int = 10


def estimate_daily_et0(t_min: float, t_max: float, t_mean: float, day_of_year: int) -> float:
    """Estimate daily reference evapotranspiration (ET0) via Hargreaves-Samani method.
    Calculated purely from air temperature and day of year.
    """
    t_range = max(0.0, t_max - t_min)
    if t_range <= 0.0:
        return 0.0

    radiation_angle = 2.0 * np.pi * (day_of_year + SOLSTICE_DAY_OFFSET) / DAYS_PER_YEAR
    ra_equivalent = SOLAR_RAD_BASE_MM_PER_DAY * (
        1.0 - SOLAR_RAD_AMPLITUDE * np.cos(radiation_angle)
    )

    et0 = (
        HARGREAVES_COEFFICIENT
        * (t_mean + HARGREAVES_TEMP_OFFSET_C)
        * np.sqrt(t_range)
        * ra_equivalent
    )
    return max(0.0, float(et0))


def simulate_water_trajectory(
    initial_depletion: float,
    taw_mm: float,
    raw_mm: float,
    et0_array: np.ndarray,
    precip_array: np.ndarray,
    kc_array: np.ndarray,
) -> tuple[np.ndarray, np.ndarray]:
    """Simulates daily root-zone soil moisture depletion following the FAO-56 model.

    Tracks depletion dynamics under variable crop cover or bare soil, calculating
    the water stress coefficient (Ks) and adjusting actual evapotranspiration (ETc).

    Args:
        initial_depletion: Starting soil root-zone depletion in mm (0 = field capacity).
        taw_mm: Total Available Water in mm (1000 * (FC - WP) * Zr).
        raw_mm: Readily Available Water in mm (p * TAW) before water stress occurs.
        et0_array: Daily reference evapotranspiration values in mm/day.
        precip_array: Daily precipitation values in mm/day.
        kc_array: Daily crop or bare soil coefficients (Kc).

    Returns:
        tuple[np.ndarray, np.ndarray]:
            - depletion_trajectory: Daily soil moisture depletion values in mm.
            - ks_trajectory: Daily water stress reduction coefficients (0.0 to 1.0).

    """
    length = len(et0_array)
    depletion_trajectory = np.zeros(length, dtype=np.float32)
    ks_trajectory = np.ones(length, dtype=np.float32)

    current_depletion = initial_depletion

    for i in range(length):
        et0 = et0_array[i]
        kc = kc_array[i]
        precip = precip_array[i]

        # Potential crop evapotranspiration under non-stressed conditions
        potential_etc = kc * et0

        # Calculate water stress coefficient (Ks) based on current depletion
        if current_depletion <= raw_mm:
            # Readily available water zone: no transpiration reduction
            ks = 1.0
        elif current_depletion < taw_mm:
            # Stress zone: linear reduction of transpiration
            ks = (taw_mm - current_depletion) / (taw_mm - raw_mm)
        else:
            # Below wilting point: transpiration ceases
            ks = 0.0

        ks_trajectory[i] = ks
        actual_etc = ks * potential_etc

        # Update root-zone water balance (FAO-56 daily depletion equation)
        current_depletion = current_depletion - precip + actual_etc

        # Bound depletion: 0.0 represents Field Capacity, taw_mm represents maximum storage loss
        if current_depletion < 0.0:
            current_depletion = 0.0
        elif current_depletion > taw_mm:
            current_depletion = taw_mm

        depletion_trajectory[i] = current_depletion

    return depletion_trajectory, ks_trajectory
