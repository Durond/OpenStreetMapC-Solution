# OsmApi Solution
## Docker

В каталоге с `docker-compose.yml` (корень решения):

```bash
docker compose up -d --build
```

**С нуля:** после клонирования репозитория нужна по сути одна команда выше. Соберутся образы API и импортёра, поднимется PostGIS, применятся миграции,  далеее через API произведется запрос который через Overpass подгрузит точки в bbox из compose.(первый раз дольше: сборка, старт БД, ответ Overpass может занять несколько минут). Убедиться, что импорт закончился: `docker compose logs importer` — в конце строка `Done. Imported/upserted: …`. Проверка API: `http://localhost:5000/api/Places/search?lat=52.28&lon=104.29&radiusMeters=3000`. Если Overpass временно недоступен, повторите `docker compose up importer` позже (или `docker compose run --rm importer`).

Поднимаются PostGIS (**5432**), API (**5000** → контейнер **8080**), pgAdmin (**8080**), один раз отрабатывает импортёр с настройками из compose. API тогда: `http://localhost:5000`, Swagger: `http://localhost:5000/swagger`.

Импорт по умолчанию идёт через **Overpass API**: в базу попадают только точки POI внутри прямоугольника `IMPORT_BBOX` (отдельного файла «только Иркутск»). Отдельно качать сибирский `.osm.pbf` не нужно. Если нужен именно файл с Geofabrik: `OSM_IMPORT_SOURCE=pbf`, `OSM_PBF_URL`, и при необходимости снова `IMPORT_BBOX` при разборе.




Примеры (координаты в центре области импорта из compose, порт API в Docker — **5000**):

- без фильтра `type` — все категории в радиусе 3 км:  
  `http://localhost:5000/api/Places/search?lat=52.28&lon=104.29&radiusMeters=3000`
- `amenity` — кафе, больницы, банкоматы и т.п.:  
  `http://localhost:5000/api/Places/search?lat=52.28&lon=104.29&radiusMeters=3000&type=amenity`
- `shop` — магазины:  
  `http://localhost:5000/api/Places/search?lat=52.28&lon=104.29&radiusMeters=3000&type=shop`
- `tourism` — отели, музеи и др.:  
  `http://localhost:5000/api/Places/search?lat=52.28&lon=104.29&radiusMeters=5000&type=tourism`
- `public_transport` — остановки и т.п.:  
  `http://localhost:5000/api/Places/search?lat=52.28&lon=104.29&radiusMeters=3000&type=public_transport`

Локально без Docker подставьте `http://localhost:5179` вместо `http://localhost:5000`.

Остановка: `docker compose down` (данные БД в именованном volume сохранятся).

## База



## API: локально без Docker

```bash
dotnet run --project OsbApi
```

**Порт 5179** — `http://localhost:5179`, Swagger: `/swagger`. В Docker см. раздел выше (**порт 5000**).

Поиск точек рядом с координатой — **GET** `/api/Places/search`, параметры в query-строке. Минимум нужны **`lat`** и **`lon`** (широта и долгота). При желании: `radiusMeters` (по умолчанию 500), `type` (например `amenity`), `minAccuracy`, `format` (`geojson`, `xml` или `pbf`).

Пример запроса в браузере или в любом HTTP-клиенте:

```
http://localhost:5179/api/Places/search?lat=42.50779&lon=1.52109&radiusMeters=1000
```

Те же правила `type` и набор категорий из импорта — см. раздел Docker выше.

