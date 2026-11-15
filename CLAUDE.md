# CLAUDE.md — Polle

Guidance for Claude (and people) working in this repository. Read it before changing code.

## What Polle is

Polle is a farm-planning platform built for NASA Space Apps Challenge 2026. A farmer maps farms and fields, records
what is sown when (with the water and fertilizer each crop gets), and asks a solver for crop-rotation **plans** over a
horizon of up to 10 years ahead (or 5 years back). Polle is the system's integration core: it keeps everything the
solver needs but doesn't know (field boundaries, planting history, soil, local climate, prices, costs, machinery),
validates it, hands it to the solver in one contract, and turns the solver's scenarios back into cards the farmer can
compare, save and apply as the field's real plan. It also has a public climate/drought map and an AI assistant.

## Sibling repository: eco_crop (keep them in sync)

`kivanval/polle` and `Denchan-san/eco_crop` run **the same product logic** with a slightly different layout. Any
change to shared logic (API, domain, persistence, web UI, copy) must be made in both, with the same behaviour and
wording. Path map:

| polle | eco_crop |
|---|---|
| `core/` (`Polle.Api`, `Polle.Domain`, `Polle.Infrastructure`, `Polle.Api.Tests`) | `backend/` (`EcoCrop.*`) |
| `web/` | `frontend/` |
| `math-bees/` (the solver, deployed as felder) | — (eco_crop only calls the deployed solver) |
| `api/*/openapi.yml` (service contracts) | — |
| — | `pipelines/`, `tools/`, `data_to_insert/`, `research/`, `docs/`, `backend/*.sql` |

Porting a commit: `git format-patch` it in one repo, rewrite paths (`frontend/`→`web/`, `backend/src/EcoCrop.`→
`core/src/Polle.`, `backend/tests/EcoCrop.`→`core/tests/Polle.`) and `EcoCrop`→`Polle`, then `git apply` in the other.
The `core`/`backend` and `web`/`frontend` trees are file-for-file identical apart from the names; keep it that way.
Both repos deploy to the **same** Cloudflare Pages project (`polle`) and the same Render service (`ecocrop-api`), so
the last deploy from either repo wins. Names that intentionally still say EcoCrop (don't "fix" them): cookies `ecocrop.session` / `ecocrop.csrf`, Data
Protection application name `EcoCrop`, migrations table `public.__ecocrop_migrations`, header `X-EcoCrop-Client-IP`,
localStorage keys `ecocrop.*`, Render service `ecocrop-api`.

## Repository layout

```
core/                 ASP.NET Core 10 Minimal API, EF Core 10, PostgreSQL + PostGIS (Neon in production)
  src/Polle.Api/          Program.cs, Common/ (IEndpoint, ApiExceptionHandler, SolverServiceKey), Features/<Area>/
  src/Polle.Domain/       Entities: Agriculture/ (Farm, Field, FieldPlanting, StrategyRun, FieldStrategy, …), Users/, Catalog/
  src/Polle.Infrastructure/ AppDbContext, EF configurations, Persistence/Migrations, NASA POWER clients
  tests/Polle.Api.Tests/  xUnit (+ Microsoft.AspNetCore.Mvc.Testing)
  src/Polle.Api/ClimateTrend/  manifest.json + model.bin (~24 MB climate model, built by eco_crop pipelines/drought)
web/                  React 19, strict TypeScript, Vite, React Router, TanStack Query, Leaflet (+ Geoman), Motion, Tailwind 4
  src/app/ pages/ features/<area>/ shared/ styles/      functions/api/[[path]].ts = Cloudflare Pages /api proxy
math-bees/            The solver (Python 3.11): FastAPI + taskiq workers on Redis, OR-Tools CP-SAT, dishka DI
api/                  OpenAPI contracts: math-bees (solver), climate, field-topology, finance (served by core to the solver), core
docker-compose.yml    Local PostGIS (`db`); `--profile full` also builds api + web
render.yaml           Render blueprint for core (service `ecocrop-api`, free plan, Frankfurt)
.github/workflows/    ci.yml (core build/test, web lint/test/build), deploy-web.yml (Cloudflare Pages project `polle`)
```

## Commands

Core (from `core/`):
```bash
dotnet restore && dotnet tool restore          # dotnet-ef 10.0.13 is a local tool
dotnet run --project src/Polle.Api             # http://localhost:5080, /scalar and /openapi in Development
dotnet build Polle.sln && dotnet test Polle.sln
dotnet ef migrations add <Name> --project src/Polle.Infrastructure --startup-project src/Polle.Api --output-dir Persistence/Migrations
dotnet ef migrations script --idempotent --project src/Polle.Infrastructure --startup-project src/Polle.Api --output <file>.sql
dotnet ef database update --project src/Polle.Infrastructure --startup-project src/Polle.Api
```
Web (from `web/`): `npm ci`, `npm run dev` (http://localhost:5173, proxies `/api` to `API_URL` or
`http://localhost:5080`), `npm test` (vitest), `npm run lint` (oxlint), `npm run build` (`tsc -b` + Vite).
Use `localhost`, not `127.0.0.1` (cookies). Vite uses a strict port.

Solver (from `math-bees/`): needs `.env` (see `.env.example` and `application.conf`: `REDIS_URL`, `DB_*`,
`FIELDS_/CLIMATE_/FINANCE_SERVICE_URL` and `_TOKEN`). `bash scripts/generate_api.sh` generates
`src/app/autogen/` (pydantic schemas + HTTP clients) from `../api/*.yml`; it is not committed. `docker compose up`
runs api, 3 worker replicas, Redis and a cloudflared tunnel. Checks: `pytest`, `ruff check`, `mypy`.

CI runs exactly: core `dotnet build -c Release` + `dotnet test`; web `npm ci`, `npm run lint`, `npm test`, `npm run build`.
Run the same before you say something works.

## Configuration

- `core/src/Polle.Api/appsettings.Local.json` (git-ignored, copy `appsettings.Local.example.json`) holds
  `ConnectionStrings:Database`; Program.cs loads it explicitly, then environment variables override
  (`ConnectionStrings__Database`). Never put secrets in the web app or in `VITE_*` variables.
- `Solver:BaseUrl` (default `https://opt-felder-api.pp.ua/`); unset makes the plan routes answer 503.
- `Finance:ApiKey`, `Climate:ApiKey`, `Fields:ApiKey`: keys the solver sends to core's service routes (see below).
- `Proxy:SharedSecret`: shared with the Cloudflare Pages proxy (`API_PROXY_SECRET`).
- `DataProtection:CertificateBase64` (required in Production): encrypts the key ring stored in `data_protection_keys`.
- `Assistant:*`: `Provider` (Ollama default, or `OpenAICompatible`), `BaseUrl`, `Model`, `ApiKey`, reasoning settings.
- The API never migrates the database on startup. Apply migrations deliberately (idempotent SQL script or
  `dotnet ef database update`), before deploying code that needs them. History table: `public.__ecocrop_migrations`.

## Backend conventions (core)

- **Vertical slices.** Each feature lives in `Features/<Area>/`; every class implementing `IEndpoint` is discovered and
  mapped under `/api` at startup. Contracts are records with `[Description]` on properties (they feed OpenAPI).
- **Validation** is manual (`*Validation.cs`) and returns field-keyed ProblemDetails errors; `[Required]` inputs are
  non-nullable in OpenAPI. Errors go through `ApiExceptionHandler`: an unreachable database is 503, other faults 500,
  the solver its own 503 with `code: solver_unavailable`. Never leak driver details.
- **Ownership** is checked on every query (`OwnedResources`): the user id comes only from session claims
  (`ClaimTypes.NameIdentifier`); ids from the request, page or assistant context are untrusted.
- **Security**: cookie session checked against `public.sessions` on every request; antiforgery header `X-CSRF-TOKEN`
  on every non-GET `/api` request (except key-authenticated service routes); rate-limit policies `auth`,
  `assistant` (30/min), `public-data` (600/min), `finance`/`climate`/`fields` (solver partitions); request bodies are
  capped at 16 KiB; field polygons at 500 vertices.
- **PostGIS**: field geometry is a GeoJSON polygon (WGS84); area is computed by PostGIS. Fields of a farm and
  subfields of a field must not overlap (checked client- and server-side). Plantings on one field must not overlap:
  a GiST exclusion constraint (`btree_gist`) enforces it in the database.
- **Service routes for the solver** (`/api/v1/fields/{field_id}/{year_month}`, `/api/v1/climate/daily/{field_id}`,
  `/api/v2/finance/...`) authenticate with a key header (`X-Fields-Api-Key`, `X-Climate-Api-Key`,
  `X-Finance-Api-Key`) via `SolverServiceKey` metadata; without a configured key they are open only in Development.
  Missing prices for a year fall back to the nearest year that has them.

## How planning works (core ↔ solver)

- Two runs per field, each stored in `strategy_runs` with its jobs, so nothing is lost on restart:
  **Evaluate my plan** (`planOnly`) scores the farmer's own plantings (job key `current-plan`), and
  **Suggest plans** runs the chosen presets (up to 12; six defaults in `StrategyCatalog`: Control, Rainfed potential,
  Medium, Full inputs, Full inputs with estimates, Cash grab) plus custom presets.
- `FelderClient` submits to `POST api/v1/solver/{optimizations|evaluations}`, polls `.../{task_id}/status` (200 =
  processing, **303 = done**, so redirects are read, not followed) and reads `.../result`. `StrategyJobs` keeps the
  state machine: transient failures resubmit with backoff (`SubmitGiveUpAfter` 3 min), a job gives up after
  `GiveUpAfter` (15 min), a failed job can be retried alone. Evaluate waits briefly in the request (`QuickWait`).
- `StrategyRequests` builds the solver request; `StrategyResultMapper` maps the result. The current plan sends each
  crop's irrigation and fertilizer with their target levels (`irrigation_target_percent` = refill of readily
  available water per irrigation, `fertilizer_target_percent` = share of the nutrient deficit covered); "Use this plan"
  copies a suggestion's amounts and levels into `field_plantings`, so a copied plan scores like the suggestion.
- The cards show **only** the risks and warnings the solver returns. When the solver can't score the current plan,
  `PlanAudit` estimates money, water and N-P-K (marked Estimated) and adds no risks of its own. Never recompute or
  "correct" solver figures.

## The solver (math-bees)

- Flow: FastAPI enqueues a taskiq task on a Redis list queue (`ListQueueBroker`), a worker runs it, the API answers
  status from the Redis result backend. `optimize_fields_task`: `CanvasPreparer` → `CroppingStudio.prepare`
  (candidate windows + simulation steps: history/rotation, phenology, soil, temperature, water, nutrient, finance) →
  `SolverPipeline` (CP-SAT components: field occupancy, crop rotation, water and nutrient accumulation, max profit).
  `evaluate_scenario_task` simulates the farmer's schedule and its carryover without CP-SAT.
- **Never run CPU-bound work on the worker's event loop.** Preprocessing and simulation go through
  `asyncio.to_thread`; CP-SAT already does. Blocking the loop starves every other task the process holds (an
  evaluation behind a batch of optimizations never starts). `tests/test_studio_event_loop.py` guards this. Workers run
  with `--max-async-tasks 4` so one process can't hoard the queue or fill its thread pool.
- Results and repository reads are cached in Redis with cashews (TTL 4 h, `caching` in `application.conf`).
- Solver settings: `solver.num-workers` 4, `time-limit-seconds` 60, `relative-gap-limit` 0.1.

## Web conventions

- Feature folders `src/features/<area>/` (`api.ts` with TanStack Query hooks and keys, components, pure helpers with
  `*.test.ts`); pages in `src/pages/`; `@/` aliases `src/`. All requests go through `shared/api/client` (`apiFetch`,
  CSRF token, `ApiError`); `problemMessage` (`features/farms/api.ts`) turns ProblemDetails into a sentence for the user.
- Tests are vitest; components are usually checked with `renderToStaticMarkup` and string/regex assertions.
- Keep each visible text node in its own element (e.g. `<span>`), so a page translator rewriting text can't break
  React's updates (see `shared/ui/domDesync`).
- Collapsible panels, focus traps in dialogs, `prefers-reduced-motion` support and visible focus rings are expected.

## Product language (UI copy)

- Say **plan(s)**, not strategies: "Suggest plans", "Save plan", "Use this plan", "Your current plan", "Plan summary";
  a solver setting combination is a **preset**. Code names (`Strategy*`, `/strategies`) stay as they are.
- Plain farm words, short sentences, no semicolons in interface text, verb-first buttons, errors that say what to do.
- Water is shown in mm and m³ (1 mm over 1 ha = 10 m³); fertilizer in kg of product (not of N, P or K).

## Working rules

- Read the surrounding code before editing; match its style (doc comments explain *why*, in plain words).
- Write or update tests with every behaviour change; run the CI commands for each part you touched.
- Database changes: an EF migration plus a reviewed idempotent SQL script; destructive `Down` migrations are blocked.
- Don't commit secrets, `appsettings.Local.json`, `.env`, `node_modules`, `bin/obj` or `math-bees/src/app/autogen`.
- Commit messages: a one-line summary of the user-visible effect, then a short body with the why.
