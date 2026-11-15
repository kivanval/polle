# Polle

A farmer's workspace and the integration core of a crop-rotation planning system, built for NASA Space Apps Challenge
2026. An independent project, not officially endorsed by NASA.

Polle keeps everything the solver needs but doesn't know on its own: field boundaries, planting history, soil, local
climate, prices, costs and farm machinery. The platform validates this data, hands it to the solver in a single contract
and returns **plans** to the farmer, who can compare them, save them and apply one to the field.

> For working with the code (including with Claude Code), see [CLAUDE.md](CLAUDE.md): architecture, commands, rules and
> keeping in sync with the sibling repository eco_crop.

## What it does

- **Farm.** Farms, fields and subfields on a map (GeoJSON, area computed by PostGIS, fields never overlap), a planting
  timeline (PostgreSQL keeps periods from overlapping even under concurrent requests), a budget across eight categories,
  actual expenses, machinery with engine-hour limits and a weekly staff shift schedule. Each owner's data is isolated.
- **Planning.** A horizon of up to 10 years ahead or up to 5 years back.
  - *Evaluate my plan* scores the plantings entered, with their irrigation and fertilizer rates and target levels (how
    far to refill the water, what share of the NPK deficit to cover).
  - *Suggest plans* runs up to 12 optimization presets (six defaults: Control, Rainfed potential, Medium, Full inputs,
    Full inputs with estimates, Cash grab) plus the farmer's own presets.
  - The cards show the change in profit, costs, irrigation and risks against the current plan, a Gantt chart, water and
    fertilizer per crop, and the expected share of yield. Only the risks the solver returns are shown.
  - Each job's state is stored in the database; transient failures are retried automatically, and a single card can be
    rerun.
- **Solver (math-bees).** FastAPI + taskiq workers on Redis, a simulation of agronomic steps and CP-SAT (OR-Tools). The
  solver reads the field's topology and soil, daily climate (NASA POWER, a 10-year average) and prices from Polle through
  protected service routes.
- **Climate Intelligence.** A public map of drought, temperature and rainfall on a 0.5° grid (NOAA CPC, 2016–2025
  normals and a damped trend to 2035), US Drought Monitor drought classes D0–D4, and NASA GIBS and Copernicus EDO layers
  with the value at any point.
- **AI assistant.** The model only recognises the intent from a closed list of actions; deterministic C# code reads the
  data and runs calculations within the user's permissions. Answers about plans are built from the solver's results.
  It also suggests hybrids and varieties from the database for a crop or a field, putting drought-tolerant ones first
  when the field's climate looks dry.

## Repository layout

| Folder | What it is |
|---|---|
| `core/` | ASP.NET Core 10 API (Minimal API), EF Core 10, PostgreSQL + PostGIS (Neon) |
| `web/` | React 19, strict TypeScript, Vite, TanStack Query, Leaflet; deployed to Cloudflare Pages |
| `math-bees/` | The solver (Python 3.11, FastAPI, taskiq, Redis, OR-Tools) |
| `api/` | OpenAPI contracts of the solver and of the service routes core exposes to the solver |

## Quick start

You need .NET SDK 10, Node.js 22+ and PostgreSQL with PostGIS (Neon, or a local `docker compose up db`).

```bash
# API
cd core
dotnet restore && dotnet tool restore
cp src/Polle.Api/appsettings.Local.example.json src/Polle.Api/appsettings.Local.json   # fill in ConnectionStrings:Database
dotnet ef database update --project src/Polle.Infrastructure --startup-project src/Polle.Api
dotnet run --project src/Polle.Api          # http://localhost:5080, API docs: /scalar

# Web app (another terminal)
cd web
npm ci
npm run dev                                 # http://localhost:5173, /api is proxied to :5080
```

The API never applies migrations on its own: apply them deliberately, before deploying the code that needs them.
By default the solver is the deployed `https://opt-felder-api.pp.ua/` (`Solver:BaseUrl`). To run your own, see
`math-bees/` (`.env`, `bash scripts/generate_api.sh`, `docker compose up`).

## Checks

```bash
cd core && dotnet build Polle.sln && dotnet test Polle.sln
cd web && npm run lint && npm test && npm run build
cd math-bees && pytest && ruff check && mypy      # after generate_api.sh
```

CI (`.github/workflows/ci.yml`) runs the core and web checks on every PR; `deploy-web.yml` publishes the site to
Cloudflare Pages after a merge into `main`. The API is deployed to Render from `render.yaml`.

## Data sources

NASA POWER, NASA GIBS, NOAA CPC, Copernicus EDO, OpenStreetMap / CARTO, Esri World Imagery, Nominatim, Natural Earth,
and the FAO-56 and US Drought Monitor methods. The full list with links is on the site's "Data & sources" page.
