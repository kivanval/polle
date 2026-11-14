const apiBaseUrl: string = import.meta.env.VITE_API_BASE_URL ?? '/api'

export const env = {
  apiBaseUrl,
  /**
   * The API host's liveness route, pinged directly (past the /api proxy) so a sleeping free-tier host starts its cold
   * start while the user is still typing. Only in production builds behind the relative /api proxy; VITE_API_WAKE_URL overrides it.
   */
  apiWakeUrl: (import.meta.env.VITE_API_WAKE_URL as string | undefined)?.trim()
    || (import.meta.env.PROD && apiBaseUrl.startsWith('/') ? 'https://ecocrop-api.onrender.com/api/health/live' : null),
  /** Optional public CARTO basemaps key (https://carto.com/basemaps/apikey). Without it the street map uses OpenStreetMap tiles. */
  cartoBasemapsKey: (import.meta.env.VITE_CARTO_BASEMAPS_KEY as string | undefined)?.trim() || null,
}
