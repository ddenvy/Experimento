# Веха 3: Reports & Export (просмотр отчёта в UI + Download .md + Print/PDF)

> Цель вехи: замкнуть последний разрыв научного цикла — пользователь должен видеть
> inspection-ready отчёт по версии (состав, условия, прогнозы, reviews, lab outcomes,
> симуляции, аудит) прямо в продукте и выгружать его наружу без Postman/curl.
> Бэкенд уже генерирует полный Markdown — веха почти целиком про доступ к нему из UI.

## Repository Research (фактическое состояние кода)

### Уже готово на бэкенде (проверено в коде)
- `GET /api/reports/formulation-versions/{versionId}/report` ([ReportsController.cs](file:///c:/Develop/Experimento/backend/src/Experimento.WebApi/Controllers/ReportsController.cs)):
  - авторизация через `ResourceAuthorization.OwnsVersionAsync` → чужому 403, несуществующей версии 404;
  - `GenerateVersionReportHandler` ([ReportFeatures.cs](file:///c:/Develop/Experimento/backend/src/Experimento.Application/Features/Reports/ReportFeatures.cs)) собирает Markdown: заголовок, цель, состав (GFM-таблица), условия, все prediction-jobs с rationale/источниками/reviews/outcome (включая calibration error), симуляции с кандидатами, последние 100 аудит-записей;
  - ответ — `File(bytes, "text/markdown; charset=utf-8", $"formulation-{versionId}.md")`, т.е. **attachment-загрузка**, а не inline-просмотр;
  - при каждом запросе пишется аудит-событие `Report.Download` (compliance-приятно: просмотр отчёта трассируется).
- Версии, прогнозы, reviews/outcome отдаются существующими DTO — новых данных для отчёта не требуется.

### Найденные пробелы
1. **Нет UI отчёта.** Эндпоинт нельзя открыть ссылкой/новой вкладкой: он требует `Authorization: Bearer`, а access-токен живёт только в памяти ([api.ts](file:///c:/Develop/Experimento/frontend/lib/api.ts#L6-L14)). Скачивание возможно лишь через авторизованный `fetch` → blob.
2. **API-клиент умеет только JSON:** внутренняя `request<T>` всегда делает `res.json()` ([api.ts, L388-416](file:///c:/Develop/Experimento/frontend/lib/api.ts#L388-L416)). Нужен текстовый вариант ответа с сохранением ретрая 401→refresh.
3. **Нет markdown-рендера.** Отчёт — Markdown с GFM-таблицами; в зависимостях нет ни `react-markdown`, ни `remark-gfm`. Писать собственный парсер markdown — антипаттерн (KISS: стандартная маленькая библиотека).
4. **Нет ни одного теста на отчётный эндпоинт** (grep по `backend/tests` — ноль совпадений): 200/контент, 403, 404 не покрыты.
5. PDF/Excel-экспорт осознанно отложен планом вехи 2. В этой вехе PDF = **печать средствами браузера** (Save as PDF в системном диалоге), без серверных PDF-движков.

## Scope вехи

**Входит:**
1. Backend: интеграционные тесты существующего эндпоинта (функционально бэкенд не меняется).
2. Frontend: текстовый запрос в API-клиенте + метод `getVersionReport(versionId): Promise<string>`.
3. Frontend: новая вкладка **Report** на странице формуляции — выбор версии, генерация по кнопке, рендер Markdown (GFM-таблицы), кнопки **Download .md** и **Print / PDF**.
4. E2E-сценарий отчёта; полный локальный гейт (tsc/lint/build/playwright), визуальная проверка desktop/mobile.

**Не входит (отдельные вехи):** серверный PDF (QuestPDF/headless Chrome), Excel-экспорт, Word/PowerPoint, шаблоны/кастомизация отчёта, e-mail-рассылка отчётов, отчёт по нескольким версиям/summary по проекту, авто-регенерация, сохранённые снимки отчётов в БД.

**Решения по продукту (зафиксированы, подлежат подтверждению):**
- Отчёт генерируется **по клику на кнопку** `Generate report`, а не авто-загрузкой при открытии вкладки: отчёт может быть тяжёлым, каждое чтение пишет аудит-событие — явное действие семантически ближе к `Report.Download`.
- PDF — через системный диалог печати браузера (Print / Save as PDF), без новых серверных компонентов.
- Добавляются две зависимости: `react-markdown` и `remark-gfm` (рендер таблиц). Raw-HTML из отчёта НЕ включается (`rehype-raw` не ставим) — имена формуляций и заметки пользователей экранируются, XSS-поверхности нет.

---

## Implementation Steps (по фазам, в порядке зависимостей)

### Фаза 1. Backend — тесты существующего эндпоинта (продакшн-код не меняется)
Новый файл `tests/Experimento.WebApi.Tests/ReportFlowTests.cs` (паттерн `OutcomeFlowTests`/ApiFixture, изоляция по пользователю):
1. Сид: пользователь A → проект → формуляция → версия (Aspirin из каталога сидера, сумма пропорций 1.0); опционально один completed-прогон (поллинг до терминального статуса, как в других тестах).
2. `GET /api/reports/formulation-versions/{id}/report`:
   - 200, `Content-Type: text/markdown`, тело содержит `# Formulation Report:`, имя формуляции, секции `## Composition`, `## Predictions`, `## Audit Trail`, имя `Aspirin`;
   - заголовок `Content-Disposition` с именем файла `formulation-{id}.md`.
3. Чужой пользователь B → **403**.
4. Случайный GUID версии → **403** (факт реализации: `OwnsVersionAsync` проверяется первой
   веткой и не раскрывает существование ресурса — единый контракт всех эндпоинтов; 404 из
   последующей загрузки сущности практически недостижим. Продакшн-код не менялся.)
Критерий: весь проект тестов зелёный, кода в `src/` не меняем.

### Фаза 2. Frontend — API-клиент и зависимости
1. `lib/api.ts`: расширить внутренний `request` опцией парсинга (минимально: `requestText(path, options?)` с тем же жизненным циклом 401→refresh→retry, но `return await res.text()`). Метод API:
   - `getVersionReport(versionId: string): Promise<string>` → `GET /reports/formulation-versions/{versionId}/report`.
   (Скачивание и печать делаются из уже полученной строки — повторных запросов/аудита нет.)
2. `npm i react-markdown remark-gfm` (зафиксировать версии в `package.json`/lockfile).

### Фаза 3. Frontend — вкладка Report
1. Новый `components/reports/report-tab.tsx` (презентационный, пропсы `versions: FormulationVersionDto[]`, `initialVersionId?: string` — как `PredictionsTab`):
   - селектор версии (`Select version`, дефолт — `?version=` или последняя версия);
   - кнопка `data-testid="generate-report"` «Generate report» со спиннером; состояния loading/error;
   - после загрузки — панель действий: `data-testid="download-report"` «Download .md» и `data-testid="print-report"` «Print / PDF»;
   - рендер через `<ReactMarkdown remarkPlugins={[remarkGfm]}>` в контейнере с типографикой (`prose`-подобные стили вручную через Tailwind, без `@tailwindcss/typography` — лишняя зависимость) и `overflow-x-auto` вокруг широких таблиц (mobile);
   - пустое состояние при отсутствии версий (как на Composition).
2. Утилиты (в том же файле, выносить не нужно):
   - **Download:** `Blob([markdown], {type: "text/markdown"})` → временный `<a download="formulation-v{n}-report.md">` → click → revoke.
   - **Print/PDF:** скрытый same-origin `<iframe>` (без popup-блокеров): записать HTML отрендеренного узла + минимальный CSS (таблицы, заголовки), `contentWindow.focus()` + `print()`; iframe удаляется после печати.
3. Встроить вкладку в страницу формуляции: добавить `{ id: "report", label: "Report" }` в `TABS` и ветку `{tab === "report" && <ReportTab ... />}` в [page.tsx](file:///c:/Develop/Experimento/frontend/app/(app)/projects/[projectId]/formulations/[formulationId]/page.tsx#L16-L20) — паттерн вкладок/`?version=` уже есть. Навигацию, палитру, дашборд не трогаем (отдельной страницы нет).

### Фаза 4. E2E и финальная валидация
1. E2E в `e2e/app.spec.ts` (сид через API, как compare/outcome-тесты):
   - открыть `?tab=report`, выбрать версию, кликнуть `generate-report`;
   - дождаться `data-testid="report-content"`: видны заголовок с именем формуляции и секция «Composition» (таблица с Aspirin);
   - клик `download-report` → `page.waitForEvent("download")`: имя файла заканчивается на `.md`;
   - проверка, что нет горизонтального скролла страницы (таблица скроллится внутри контейнера).
   - Печать в E2E не кликаем (системный диалог завис бы); покрытие кнопки — наличие + enabled.
2. Гейт из `frontend`: `npx tsc --noEmit`, `npm run lint`, `npm run build`, затем полный `$env:CI=''; npx playwright test --workers=1` против пересобранного `docker compose up -d --build frontend`.
3. Backend: `dotnet test` по всем тест-проектам (`backend/tests/*`).
4. Визуальная проверка 1440/390: вкладка Report с длинным отчётом, таблицы, панель кнопок; mobile — без горизонтального скролла страницы.

## Files and Modules

### Backend
- `tests/Experimento.WebApi.Tests/ReportFlowTests.cs` — **новый** (3 проверки). Файлы `src/` не изменяются.

### Frontend
- `package.json`, `package-lock.json` — `react-markdown`, `remark-gfm`.
- `lib/api.ts` — текстовый вариант запроса + `getVersionReport`.
- `components/reports/report-tab.tsx` — **новый**: вкладка отчёта, рендер, download, print.
- `app/(app)/projects/[projectId]/formulations/[formulationId]/page.tsx` — 4-я вкладка.
- `e2e/app.spec.ts` — 1 новый сценарий.

Миграций БД и изменений схемы/конфигов/CI НЕ требуется.

## Dependencies and Considerations
- Отчёт приходит одним запросом по клику и переиспользуется для preview/download/print — каждое действие пользователя создаёт ровно одно аудит-событие `Report.Download` (соответствует текущей семантике эндпоинта).
- `react-markdown` по умолчанию не рендерит сырой HTML (нет `rehype-raw`) — контент с пользовательскими данными (имена, notes) безопасен; санитизация на стороне клиента не нужна.
- Имя файла для скачивания задаёт фронт (`formulation-v{n}-report.md` — читаемее, чем GUID с бэкенда); `Content-Disposition` бэкенда при `fetch`→blob ни на что не влияет.
- Печать через скрытый iframe, а не новое окно: не упирается в popup-blocker; стили для печати инлайнятся в документ iframe, приложение не нуждается в глобальных print-CSS.
- CI уже умеет `npm ci`/`build` — новые пакеты подхватятся из lockfile автоматически, правок workflow нет.

## Validation
- Backend: `dotnet test` всех 4 проектов — 0 падений, включая новый ReportFlowTests.
- Frontend: `tsc --noEmit`, ESLint без предупреждений, `next build` — успешно.
- Playwright: весь набор (текущие 18 + 1 новый) зелёный, workers=1.
- Ручной сквозной сценарий: формуляция с прогнозом и исходом → вкладка Report → Generate → видны все секции → Download .md (файл открывается как Markdown) → Print открывает системный диалог с чистым отчётом без chrome приложения.

## Risks
- **Внешние Gemini/PubChem во флапающем тесте:** основной тест отчёта не требует completed-прогноза (секции Predictions/Simulations просто пустые) — сетевые внешние сервисы не нужны; проверка секции прогноза в отчёте опциональна и толерантна к Failed, как в существующих тестах.
- **`npm i` новых пакетов меняет lockfile:** обязательно поставить до `npm ci`-сценариев и закоммитить lockfile вместе с кодом (коммит — по отдельной явной просьбе).
- **Длинные таблицы на mobile:** решается контейнером `overflow-x-auto` (проверяется e2e и визуально), глобального горизонтального скролла быть не должно.
- **Print в headless-окружении:** E2E печать не вызывает; проверяется только ручным прогоном и тем, что функция изолирована в утилите.
