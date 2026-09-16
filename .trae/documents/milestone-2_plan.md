# Веха 2: Lab Journal + Model Scorecard + Compare версий + CI

> Цель вехи: замкнуть научный цикл «предсказал → проверил в лаборатории → внёс исход →
> модель измерима», добавить доверие к модели (scorecard), сравнение версий и базовый CI.

## Repository Research (фактическое состояние кода)

### Уже готово на бэкенде (проверено в коде)
- `POST /api/predictions/prediction-results/{resultId}/review` — `SubmitReviewCommand(ResultId, ReviewerUserId, Decision, Comment)`; `Decision` парсится в enum `ReviewDecision { Approved, Rejected, NeedsRevision }`; аудит-событие пишется.
- `POST .../outcome` — `RecordOutcomeCommand(ResultId, ActualSuccess, ActualMetricsJson, Notes, RecordedBy)` → сущность `ExperimentOutcome`.
- `GET /api/predictions/calibration` — глобальные агрегаты: `CalibrationStatsDto(Total, WithOutcome, MeanError, MeanBias)` в скоупе пользователя (`GetCalibrationStatsHandler`).
- `GET /api/formulations/{formulationId}/compare?a={id}&b={id}` → `VersionComparisonDto(VersionA, VersionB, ComponentDiffs[])` с типами изменений `Added/Removed/Modified` и пропорциями обеих сторон; есть авторизация `OwnsFormulationAsync`.
- `GET /api/models` → список `ModelRegistrationDto`; `ModelRegistration` содержит `Name, Version, Description, ContextOfUse`.
- `GET /api/reports/formulation-versions/{versionId}/report` — готовый Markdown-отчёт (включая секции reviews/outcome).
- История прогонов (`ListPredictionRunsQuery`) уже отдаёт `HasOutcome` — фронт показывает зелёную галочку.

### Найденные пробелы и дефекты
1. **Баг повторного outcome (→ 500).** В миграции `Init` индекс `IX_ExperimentOutcomes_ResultId` **unique**, а `RecordOutcomeHandler` безусловно делает `Add`. Повторная запись исхода для того же результата бросит дубликат ключа (`DbUpdateException`). Нужен upsert.
2. **Read-разрыв Lab Journal.** `GetPredictionResultHandler` инклудит только `ModelRegistration` и `RationaleItems`. Записанные reviews и outcome через API не прочитать — UI не сможет показать ранее введённые данные.
3. **Нет валидаторов** для `SubmitReviewCommand` и `RecordOutcomeCommand` (мусорный `Decision`, не-JSON в `ActualMetricsJson` уходят в БД/дают невнятные ошибки).
4. **Калибровка только глобальная.** Нет разбивки по моделям — для Scorecard нужен агрегат на модель.
5. **Нет UI** для review/outcome, scorecard и compare; `api.listModels()` на фронте возвращает `unknown[]`.
6. **CI отсутствует полностью** (нет папки `.github`).
7. Интеграционные тесты (`ApiFixture`) ходят в реальные Postgres/RabbitMQ на localhost с кредами `experimento/experimento`; каталог веществ засевается сидером; внешние Gemini/PubChem — толерантны (история поллится до Completed/Failed, результат сохраняется до rationale).
8. Solution-файла (`.sln`) нет; 4 тест-проекта: Domain, Application, Infrastructure (внешние зависимости не нужны), WebApi.Tests (нужны Postgres+RabbitMQ).

## Scope вехи

**Входит:**
1. CI (backend tests + frontend typecheck/lint/build).
2. Backend: upsert outcome, отзывы+исход в DTO результата, валидаторы, scorecard-запрос, тесты.
3. Frontend: панель Review & Lab outcome в результате прогона.
4. Frontend: страница Model Scorecard + навигация.
5. Frontend: сравнение версий на вкладке Composition.

**Не входит (отдельные вехи):** PDF/Excel-экспорт (готов только Markdown-эндпоинт; кнопку экспорта не делаем), уведомления/почта, команды и роли, OCR, онбординг с демо-проектом, запуск Playwright в CI, пуш Docker-образов/деплой.

---

## Implementation Steps (по фазам, в порядке зависимостей)

### Фаза 0. CI — быстрая победа и страховка для остальных фаз
1. Создать `.github/workflows/ci.yml` с двумя джобами:
   - **backend**: `ubuntu-latest`, .NET 10 SDK; сервисы-контейнеры `pgvector/pgvector:pg17` (Postgres 5432, БД/юзер/пароль `experimento`, healthcheck `pg_isready`) и `rabbitmq:3-management` (5672, креды `experimento`); env `ConnectionStrings__Default`, `RabbitMq__*`, `Jwt__Key` (32+ символа, из github secrets не обязателен для тестов), `ASPNETCORE_ENVIRONMENT=Development`; шаг — `dotnet test` по всем 4 csproj (или циклом по каталогу `tests/*`).
   - **frontend**: `ubuntu-latest`, Node 22, `npm ci`, `npx tsc --noEmit`, `npm run lint`, `npm run build`. Playwright в CI НЕ запускаем (хрупкость docker-in-ddocker) — остаётся локальным гейтом; зафиксировать это комментарием в workflow.
2. Триггеры: `push`/`pull_request` на `main`.
3. Проверка: YAML проходит локальный парсинг; все команды джоб уже зелёные локально (подтвердить в фазе валидации).

### Фаза 1. Backend — замыкание контура исходов
1. **DTO** (`src/Experimento.Application/DTOs/Dtos.cs`): добавить поля в `PredictionResultDto` — `IReadOnlyList<ReviewDto> Reviews` и `OutcomeDto? Outcome` (nullable — у старых прогнозов исхода нет). Новый `ModelScorecardDto(Guid ModelId, string DisplayName, string Version, string ContextOfUse, int Total, int WithOutcome, double MeanError, double MeanBias)`.
2. **`GetPredictionResultHandler`**: добавить `.Include(r => r.Reviews).Include(r => r.Outcome)`; заполнить новые поля (reviews, отсортированные по `CreatedAtUtc`; outcome или null).
3. **`RecordOutcomeHandler` (upsert)**: загрузить существующий `ExperimentOutcome` по `ResultId`; если есть — обновить `ActualSuccess/ActualMetricsJson/Notes/RecordedAtUtc`, иначе добавить новый. Авторизация и аудит сохраняются как сейчас.
4. **Валидаторы** (`PredictionFeatures.cs`, авто-подхватываются существующим `ValidationBehavior`):
   - `SubmitReviewCommandValidator`: `Decision` непустой и парсится в `ReviewDecision`; `Comment` ≤ 1000 символов.
   - `RecordOutcomeCommandValidator`: `ActualMetricsJson` — валидный JSON-объект (`JsonDocument.Parse`, `ValueKind == Object`), ≤ 4000 символов; `Notes` ≤ 2000.
5. **Scorecard-запрос** (`Features/Models/ModelFeatures.cs`): `GetModelScorecardsQuery(UserId)` + handler; scope фильтрации результатов — такой же, как в `GetCalibrationStatsHandler` (`Job.RequestedBy == user || Job.Version.Formulation.Project.CreatedBy == user`); группировка по `ModelRegistrationId` in-memory (паттерн калибровки), все модели попадают в выдачу даже без outcome (нули/`0`). Эндпоинт `[HttpGet("scorecard")]` в `ModelsController`.
6. **Интеграционные тесты** (`tests/Experimento.WebApi.Tests/FullFlowTests.cs` или новый `OutcomeFlowTests.cs`):
   - запись review (200, валидный/невалидный decision → 400);
   - запись outcome и повторная запись того же результата — **200 и обновление, не 500**;
   - `GET prediction-jobs/{id}/result` после записи возвращает reviews и outcome;
   - `GET /predictions/calibration` учитывает исход (WithOutcome увеличился);
   - `GET /api/models/scorecard`: строка модели с агрегатами; чужой пользователь не видит чужих результатов;
   - compare: два состава → корректные `Added/Removed/Modified`; 403 для чужой формуляции.
   Критерий: все тесты проекта зелёные (`dotnet test`).

### Фаза 2. Frontend — типы и API-клиент (`lib/api.ts`)
1. Типы: `ReviewDto`, `OutcomeDto`, `ModelScorecardDto`, `VersionComparisonDto`, `ComponentDiff`; поля `reviews/outcome` в `PredictionResultDto`; заменить `unknown[]` у `listModels` на `ModelRegistrationDto[]` (новый тип).
2. Методы: `submitReview(resultId, {decision, comment})`, `recordOutcome(resultId, {actualSuccess, actualMetrics, notes})`, `getModelScorecard()`, `compareVersions(formulationId, a, b)`. `actualMetrics` на клиенте собирается в JSON-строку.

### Фаза 3. Frontend — панель Review & Lab outcome
1. Новый `components/predictions/outcome-panel.tsx` (presentational, пропсы: `result: PredictionResultDto`, `onChanged: () => Promise<void>`):
   - блок **Review**: select `Approved/NeedsRevision/Rejected` + comment + `Submit review`; ниже список уже принятых решений (дата, решение, комментарий);
   - блок **Lab outcome**: переключатель Actual success (`Yes/No`), три опциональных числовых поля (`Observed toxicity`, `Observed stability`, `Observed side risk`), textarea Notes; кнопка `Save outcome` / `Update outcome` (если исход уже есть — форма префиллится, это редактирование);
   - состояния loading/error/успеха; после сохранения — `onChanged()` (родитель перезагружает результат и историю → галочка `hasOutcome`).
2. Встроить панель в `predictions-tab.tsx` внутри карточки результата (под rationale, над кнопкой Continue), только для показанного Completed-результата.
3. E2E (`e2e/app.spec.ts`): после completed-прогноза записать исход (Success=Yes) → зелёная галочка в истории; уйти и вернуться на прогон — форма показывает сохранённые значения; повторное сохранение не приводит к ошибке.

### Фаза 4. Frontend — Model Scorecard
1. Новая страница `app/(app)/models/page.tsx`:
   - карточка глобальной калибровки: Total predictions, Lab outcomes, Mean error, Mean bias (использовать `getCalibration()`);
   - таблица моделей из `getModelScorecard()`: модель/версия, Context of use, Predictions, Outcomes, MAE, Bias; при `WithOutcome < 5` — бейдж «Insufficient lab data», нули показывать как «—»;
   - пустое состояние, если модели ещё не зарегистрированы.
2. Навигация: пункт `Models` (иконка `Gauge`) в `components/layout/Sidebar.tsx` (desktop + mobile drawer); статический пункт в `command-palette.tsx`; ссылка на `/models` в Quick access на дашборде (в блоке калибровки).
3. E2E: пункт виден в nav; страница открывается; после записанного исхода строка модели показывает `Outcomes ≥ 1` и ненулевой MAE.

### Фаза 5. Frontend — сравнение версий
1. Новый `components/formulations/version-compare.tsx` (пропсы: `formulationId`, `versions`): два select (по умолчанию последняя и предпоследняя версия), кнопка `Compare` (disabled при <2 версий, с подсказкой); результат — таблица компонентов: бейджи `Added` (зелёный)/`Removed` (красный)/`Modified` (жёлтый), колонки пропорций A и B и дельта (п.п.); над таблицей — условия обеих версий (temperature/pH/solvent из `VersionA/B`, данные уже в DTO, бэкенд не меняется).
2. Встроить блок в начало вкладки Composition страницы формуляции.
3. E2E: создать вторую версию (через API-сид, как уже делают другие тесты), сравнить v1/v2 — видны строки Added/Removed с корректными пропорциями.

### Фаза 6. Финальная валидация
1. Backend: `dotnet build` решения и `dotnet test` всех 4 проектов — всё зелёное.
2. Frontend: `npx tsc --noEmit`, `npm run lint`, `npm run build`, затем полный `npx playwright test` против пересобранного docker-compose стека (`docker compose up -d --build backend frontend`).
3. Визуальная проверка 1440/390: панель исхода, страница `/models`, блок compare; отсутствие горизонтального скролла на мобильном.
4. Обновить план при существенном изменении объёма; коммит — только по явной просьбе.

## Files and Modules

### Backend
- `src/Experimento.Application/DTOs/Dtos.cs`: новые поля `PredictionResultDto`, новый `ModelScorecardDto`.
- `src/Experimento.Application/Features/Predictions/PredictionFeatures.cs`: include reviews/outcome в read-handler, upsert в `RecordOutcomeHandler`, два валидатора.
- `src/Experimento.Application/Features/Models/ModelFeatures.cs`: `GetModelScorecardsQuery` + handler.
- `src/Experimento.WebApi/Controllers/ModelsController.cs`: `GET scorecard`.
- `tests/Experimento.WebApi.Tests/`: новые/расширенные тесты потока outcome/review/scorecard/compare.

### Frontend
- `lib/api.ts`: типы и 4 метода.
- `components/predictions/outcome-panel.tsx`: новый компонент.
- `components/predictions/predictions-tab.tsx`: встраивание панели, перезагрузка после сохранения.
- `app/(app)/models/page.tsx`: новая страница.
- `components/layout/Sidebar.tsx`, `components/layout/command-palette.tsx`, `app/(app)/dashboard/page.tsx`: навигация.
- `components/formulations/version-compare.tsx`: новый компонент.
- `app/(app)/projects/[projectId]/formulations/[formulationId]/page.tsx`: монтирование compare на Composition.
- `e2e/app.spec.ts`: три новых e2e-сценария.

### CI
- `.github/workflows/ci.yml`: единственный новый файл.

Миграций БД НЕ требуется (схема не меняется; уникальный индекс outcome уже существует).

## Dependencies and Considerations
- Расширение `PredictionResultDto` — обратно совместимое добавление полей; единственный потребитель маппинга — `GetPredictionResultHandler` и фронтовый тип; `ReportFeatures` строит Markdown самостоятельно и не затрагивается.
- Выбран **upsert**, а не 409: лабораторный журнал подразумевает исправление ошибочно введённого исхода; аудит-действие `Prediction.Outcome` пишется при каждой записи — история правок сохраняется через Audit Trail.
- Scorecard и калибровка считают агрегаты in-memory по выборке пользователя — консистентно с существующим `GetCalibrationStatsHandler`; объёмы данных на текущем этапе малы. Если выборка вырастет — отдельной задачей перенос агрегатов в SQL.
- `ActualMetricsJson` на фронте — компактный объект `{toxicity?, stability?, sideRisk?}`; калибровка использует только `ActualSuccess`, свободные числовые поля на неё не влияют.
- CI без GEMINI_API_KEY: это уже проверенная конфигурация — consumer сохраняет результат до падения rationale, тесты поллят терминальный статус Completed/Failed.
- Нет `.sln` — CI гоняет тесты перечислением csproj (`dotnet test tests/Experimento.*.Tests/*.csproj` не сработает одним glob'ом детерминированно; использовать цикл bash или `dotnet test` с явными путями).

## Validation
- `dotnet test` по 4 тест-проектам — 0 падений (критично: новый тест на повторный outcome).
- `npx tsc --noEmit` и `next build` — без ошибок типов; ESLint без новых предупреждений.
- Playwright: весь набор (текущие 15 + ~3 новых) зелёный.
- Ручной сквозной сценарий: prediction → review approved → outcome success=true → галочка в истории → `/models` показывает MAE → compare v1/v2.

## Risks
- **Внешние API (Gemini/PubChem) флапают в e2e:** уже смягчено retries=1 и seeded-каталогом; новые тесты завязывать на UI-фиксации статуса Completed/Failed, а не на содержимое rationale.
- **CI-джоба WebApi.Tests не поднимет сервисы:** healthcheck'и обязательны; креды строго как в `ApiFixture` (`experimento/experimento`, localhost по умолчанию внутри runner); проверить имя образа `pgvector/pgvector:pg17`.
- **Размывание объёма:** не добавлять PDF, дашборд-перестройку и уведомления в эту веху; предложение кнопки Markdown-отчёта осознанно отложено.
- **Ввод числовых observed-метрик:** только опциональные поля; сабмит исхода требует лишь Actual success — иначе форма станет барьером для записи исхода.
