# Polle

Робоче середовище фермера й інтеграційне ядро системи планування сівозміни, створене для NASA Space Apps Challenge 2026.
Незалежний проєкт без офіційного схвалення NASA.

Polle зберігає все, що потрібно солверу, але чого він сам не знає: межі полів, історію посівів, ґрунт, локальний клімат,
ціни, витрати й техніку господарства. Платформа перевіряє ці дані, передає їх солверу в єдиному контракті й повертає
фермерові **плани**, які можна порівняти, зберегти й застосувати до поля.

> Для роботи з кодом (зокрема з Claude Code) дивіться [CLAUDE.md](CLAUDE.md): архітектура, команди, правила й
> синхронізація з сестринським репозиторієм eco_crop.

## Що вміє

- **Господарство.** Ферми, поля й підполя на карті (GeoJSON, площа з PostGIS, поля не перетинаються), хронологія
  посівів (PostgreSQL не дає періодам накластися навіть за одночасних запитів), бюджет за вісьмома статтями, фактичні
  витрати, техніка з лімітами мотогодин і тижневий графік змін персоналу. Дані кожного власника ізольовані.
- **Планування.** Горизонт до 10 років уперед або до 5 назад.
  - *Evaluate my plan* оцінює внесені посіви з їхніми нормами поливу й добрив та цільовими рівнями (до якого рівня
    доливати воду, яку частку дефіциту NPK покривати).
  - *Suggest plans* запускає до 12 пресетів оптимізації (шість базових: Control, Rainfed potential, Medium, Full inputs,
    Full inputs with estimates, Cash grab) і власні пресети фермера.
  - Картки показують дельти прибутку, витрат, поливу й ризиків відносно поточного плану, діаграму Ганта, воду й
    добрива по кожній культурі, очікуваний відсоток урожаю. Ризики — лише ті, що повернув солвер.
  - Стан кожної задачі зберігається в базі; тимчасові збої повторюються автоматично, окрему картку можна перезапустити.
- **Солвер (math-bees).** FastAPI + taskiq-воркери на Redis, симуляція агрономічних кроків і CP-SAT (OR-Tools).
  Солвер читає з Polle топологію й ґрунт поля, щоденний клімат (NASA POWER, середнє за 10 років) і ціни через
  захищені сервісні маршрути.
- **Climate Intelligence.** Публічна карта посухи, температури й опадів на сітці 0,5° (NOAA CPC, норми 2016–2025 і
  демпфований тренд до 2035), класи посухи US Drought Monitor D0–D4, шари NASA GIBS і Copernicus EDO зі значенням у точці.
- **AI-асистент.** Модель лише розпізнає намір із закритого переліку дій; дані читає й розрахунки запускає
  детермінований C#-код у межах прав користувача. Відповіді про плани збираються з результатів солвера.

## Склад репозиторію

| Каталог | Що це |
|---|---|
| `core/` | API на ASP.NET Core 10 (Minimal API), EF Core 10, PostgreSQL + PostGIS (Neon) |
| `web/` | React 19, strict TypeScript, Vite, TanStack Query, Leaflet; деплой на Cloudflare Pages |
| `math-bees/` | Солвер (Python 3.11, FastAPI, taskiq, Redis, OR-Tools) |
| `api/` | OpenAPI-контракти солвера та сервісних маршрутів, які core віддає солверу |

## Швидкий старт

Потрібні .NET SDK 10, Node.js 22+ і PostgreSQL з PostGIS (Neon або локальний `docker compose up db`).

```bash
# API
cd core
dotnet restore && dotnet tool restore
cp src/Polle.Api/appsettings.Local.example.json src/Polle.Api/appsettings.Local.json   # впишіть ConnectionStrings:Database
dotnet ef database update --project src/Polle.Infrastructure --startup-project src/Polle.Api
dotnet run --project src/Polle.Api          # http://localhost:5080, документація API: /scalar

# Сайт (інший термінал)
cd web
npm ci
npm run dev                                 # http://localhost:5173, /api проксіюється на :5080
```

API не застосовує міграції сам — робіть це свідомо, перед деплоєм коду, якому вони потрібні.
Солвер за замовчуванням — розгорнутий `https://opt-felder-api.pp.ua/` (`Solver:BaseUrl`). Щоб підняти свій, дивіться
`math-bees/` (`.env`, `bash scripts/generate_api.sh`, `docker compose up`).

## Перевірки

```bash
cd core && dotnet build Polle.sln && dotnet test Polle.sln
cd web && npm run lint && npm test && npm run build
cd math-bees && pytest && ruff check && mypy      # після generate_api.sh
```

CI (`.github/workflows/ci.yml`) запускає перевірки core та web на кожен PR; `deploy-web.yml` публікує сайт на
Cloudflare Pages після злиття в `main`. API розгортається на Render за `render.yaml`.

## Джерела даних

NASA POWER, NASA GIBS, NOAA CPC, Copernicus EDO, OpenStreetMap / CARTO, Esri World Imagery, Nominatim, Natural Earth,
методики FAO-56 і US Drought Monitor. Повний перелік із посиланнями — на сторінці «Data & sources» сайту.
