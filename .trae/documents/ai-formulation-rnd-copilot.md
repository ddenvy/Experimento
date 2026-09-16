# План: AI-Driven Formulation & R&D Co-Pilot

## 1. Резюме

Создание с нуля (greenfield, каталог `c:\Develop\Experimento` пуст) интеллектуальной платформы для ученых-химиков, биологов и фармацевтов. Система проектирует составы (лекарства, материалы, катализаторы), прогнозирует вероятность успеха синтеза, токсичность и стабильность, выполняет стресс-тест симуляции с вариациями параметров, выдает детальное обоснование (Rationale Tracking) со ссылками на источники и ведет неизменяемый audit trail для комплаенса.

**Стек (зафиксирован):**
- Backend: .NET 10 / C#, Clean Architecture (Domain / Application / Infrastructure / Ai / WebApi)
- Очереди: MassTransit + RabbitMQ (асинхронные тяжелые расчеты)
- БД: PostgreSQL 17 + pgvector (реляционные данные + векторный поиск)
- ИИ: Microsoft Semantic Kernel, провайдер-агностичная абстракция (конкретный LLM настраивается конфигом позже; поддержка локальных моделей для защиты коммерческой тайны)
- Frontend: Next.js (App Router) + TypeScript strict + Tailwind + shadcn/ui + Recharts
- Аутентификация: JWT access + refresh в httpOnly cookie, пароли BCrypt

**Критерии успеха:**
1. Ученый создает формулу (компоненты, пропорции, молярные массы, условия, цель) — данные сохранены и версионированы.
2. Запуск прогноза через очередь: интерфейс не блокируется, прогресс приходит в реальном времени (SignalR), результат содержит вероятность успеха, токсичность, стабильность, побочные риски.
3. Стресс-тест: симуляция с вариациями параметров находит лучшую комбинацию (максимизация целевого показателя).
4. Rationale Tracking: каждый вывод объяснен и ссылается на источники (загруженные статьи/патенты через pgvector RAG) и прошлые прогоны.
5. Append-only audit trail с хеш-цепочкой + эндпоинт проверки целостности.
6. Human-in-the-loop: эксперт ревьюит и утверждает/отклоняет прогнозы ИИ; фактические результаты экспериментов заносятся обратно в систему (обратная связь «прогноз vs реальность»).
7. Регуляторный отчет: генерация инспекционно-готового отчета (состав, прогнозы, обоснования, симуляции, выдержка аудита).
8. Все тесты зеленые, линтеры чистые, Swagger актуален.

---

## 2. Текущее состояние

- Каталог `c:\Develop\Experimento` пуст — проект создается с нуля.
- Установлены: .NET SDK 10.0.301, Node v24.17.0.
- Требуется Docker для локального PostgreSQL (pgvector) и RabbitMQ (docker-compose).

---

## 3. Исследование рынка: боли, которые закрывает платформа

Анализ открытых источников (2025–2026) выявил ключевые боли ученых и фармкомпаний, на которые отвечает продукт:

| # | Боль (источник) | Как закрывает платформа |
|---|---|---|
| 1 | **«Черный ящик» ИИ**: регуляторы (FDA, EMA, NICE, G-BA) отклоняют непрозрачные прогнозы; требуется объяснимость и прослеживаемость каждого утверждения до источника (claim-level source attribution) | Rationale Tracking: каждый пункт обоснования ссылается на конкретный источник (документ, чанк, similarity) и прошлые прогоны; ничего не выдается «просто так» |
| 2 | **Регуляторные требования 2026**: FDA draft guidance (янв. 2025) + 10 совместных принципов FDA–EMA (янв. 2026): traceability, human oversight, data governance, lifecycle management, ALCOA+ / 21 CFR Part 11 | Append-only аудит с хеш-цепочкой, реестр моделей с context of use, human-in-the-loop ревью, версионирование всего |
| 3 | **Дорогой trial-and-error**: физические испытания можно сократить до ~50% за счет виртуальной валидации, но ученые не доверяют прогнозам без механистического обоснования | Стресс-тест симуляции до лаборатории + объяснимые факторы скоринга (каждое правило эвристики дает свой вклад в итог) |
| 4 | **Конфиденциальность / коммерческая тайна**: структуры молекул нельзя загружать в публичные облачные ИИ; нужен data sovereignty | Полностью локальный деплой (docker-compose), абстракция LLM-провайдера с поддержкой локальных моделей (Ollama) — данные не покидают контур |
| 5 | **Разрозненные данные (data silos)**: ELN/LIMS/Excel/почта — ученые тратят сотни часов на поиск; данные не машиночитаемы | Единая платформа: формулы, прогнозы, симуляции, база знаний, аудит — в одной структурированной БД (компоненты с CAS-номерами, молярными массами — машиночитаемые данные) |
| 6 | **Дефицит данных и разрыв компетенций**: модели не на чем обучать; ученые не ИИ-специалисты | Feedback loop: фактические результаты экспериментов заносятся в систему («прогноз vs реальность»), накапливается собственный датасет; интерфейс для ученых, а не для ML-инженеров |
| 7 | **Воспроизводимость**: регуляторы требуют воспроизводимости расчетов | Детерминированные симуляции (фиксированный сид), версия модели и данных фиксируется в каждом результате |
| 8 | **Совместная работа**: версии формул теряются, нет единой истории решений | Версионирование формул, ревью-решения с комментариями, полная история в аудите |

Источники: [1] [2] [3] [4] [5] [6] [7] [8] (см. конец документа).

---

## 4. Функциональность

### 4.1 Ядро (исходные требования)

1. **Ввод эксперимента**: конструктор формул — компоненты (название, CAS, хим. формула, молярная масса, пропорция), условия (температура, давление, pH, растворитель), целевое назначение. Версионирование составов.
2. **Прогноз свойств**: вероятность успеха синтеза, ожидаемая токсичность, стабильность, побочные риски — асинхронно через очередь, с live-прогрессом.
3. **Стресс-тест симуляция**: вариации параметров (концентрации ±доли граммов, температура и т.д.) для поиска лучшей комбинации по целевому показателю.
4. **Rationale Tracking**: структурированное обоснование со ссылками на базу знаний (статьи/патенты через векторный поиск) и прошлые прогоны.
5. **Data Integrity**: append-only аудит с хеш-цепочкой SHA-256, проверка целостности.

### 4.2 Новые фичи (по итогам исследования рынка)

**F1. Human-in-the-loop ревью прогнозов** (боль #2: FDA–EMA принцип human oversight)
- После готовности прогноза ученый видит карточку ревью: утвердить / отклонить / отправить на доработку + комментарий.
- Решение фиксируется в БД и аудите; отклоненные прогнозы помечаются и учитываются ИИ-агентом (плагин истории) при следующих обоснованиях.
- Как работает: `POST /api/prediction-results/{id}/review` → запись `PredictionReview` → событие в аудит → доступность решения в `HistoryPlugin`.

**F2. Feedback loop «прогноз vs реальность»** (боль #6: дефицит данных)
- После реального лабораторного прогона ученый заносит фактический результат: успех/неудача, фактические метрики (стабильность, выход реакции и т.д.).
- Система считает ошибку прогноза, показывает калибровочную статистику на дашборде (средняя ошибка, направление смещения).
- Накопленные исходы используются `HistoryPlugin` и служат будущим датасетом для обучения реальной модели.
- Как работает: `POST /api/prediction-results/{id}/outcome` → запись `ExperimentOutcome` → пересчет статистики калибровки.

**F3. Реестр моделей с context of use** (боль #2: FDA credibility assessment)
- Каждая прогнозная модель регистрируется: имя, версия, описание, контекст применения (для каких задач валидна), дата регистрации.
- Каждый `PredictionResult` ссылается на конкретную версию модели → полная прослеживаемость «какой моделью и на каких данных получено число».
- Как работает: сущность `ModelRegistration`; эвристический предиктор регистрируется как модель `rule-based-v1` при старте (миграция-сид); страница `/models` со списком.

**F4. Регуляторный отчет (inspection-ready documentation)** (боли #1, #2)
- Генерация отчета по версии формулы: состав, все прогнозы с метриками, полный rationale со ссылками на источники, результаты симуляций, ревью-решения, фактические исходы, выдержка из аудита с подтверждением целостности.
- Формат: Markdown + HTML (скачивание). Это прямой ответ на требование «документация, готовая к инспекции».
- Как работает: `GET /api/formulation-versions/{id}/report` → серверная сборка отчета из существующих данных → скачивание файла.

**F5. Сравнение версий формул** (боль #8: совместная работа)
- Бок-о-бок сравнение двух версий: дифф компонентов (добавлено/удалено/изменено), сравнение метрик последних прогнозов.
- Как работает: `GET /api/formulations/{id}/compare?a={versionId}&b={versionId}` → вычисление диффа на сервере → визуализация на фронте.

**F6. Data sovereignty / локальный контур** (боль #4: коммерческая тайна)
- Весь стек поднимается локально через docker-compose; LLM-провайдер — конфигурируемый (включая Ollama для полностью автономной работы); никаких внешних вызовов по умолчанию.
- Не отдельный модуль, а архитектурное свойство, зафиксированное в конфигурации и документации.

---

## 5. Целевая архитектура

```
┌────────────────────────────┐        REST + SignalR        ┌──────────────────────────────┐
│  Next.js Frontend          │ ───────────────────────────► │  Experimento.WebApi (.NET 10)│
│  (App Router, TS strict)   │ ◄─────────────────────────── │  Controllers / Hub / Swagger │
└────────────────────────────┘   openapi-fetch клиент       └──────────┬───────────────────┘
                                                                        │ DI
                                             ┌──────────────────────────┼──────────────────────────┐
                                             ▼                          ▼                          ▼
                                   Application (use cases)   Infrastructure              Ai (Semantic Kernel)
                                             │                EF Core + pgvector           агенты, плагины, RAG
                                             │                MassTransit consumers        провайдер-абстракция
                                             ▼                аудит, auth, embedding
                                   Domain (entities, rules)            │
                                                                       ▼
                                              RabbitMQ ◄── очереди расчетов ──► PostgreSQL 17 + pgvector
```

Поток расчета: `POST /predictions` → создание `PredictionJob(Pending)` → публикация команды в RabbitMQ → consumer выполняет прогноз + SK-обоснование → прогресс через SignalR → результат в БД → фронт отображает метрики и rationale → ученый делает ревью (F1) → после лаборатории заносит фактический исход (F2).

---

## 6. Структура репозитория

```
Experimento/
├── docker-compose.yml              # postgres (pgvector/pgvector:pg17), rabbitmq:3-management
├── .gitignore
├── README.md
├── backend/
│   ├── Experimento.sln
│   ├── src/
│   │   ├── Experimento.Domain/           # сущности, перечисления, доменные правила
│   │   ├── Experimento.Application/      # интерфейсы портов, DTO, команды/запросы, валидация (FluentValidation)
│   │   ├── Experimento.Infrastructure/   # EF Core, pgvector, MassTransit consumers, аудит, auth, embedding
│   │   ├── Experimento.Ai/               # Semantic Kernel: фабрика провайдеров, агенты, плагины, RAG
│   │   └── Experimento.WebApi/           # контроллеры, SignalR hub, Swagger, middleware, DI-композиция
│   └── tests/
│       ├── Experimento.Domain.Tests/
│       ├── Experimento.Application.Tests/
│       └── Experimento.Infrastructure.Tests/
└── frontend/                             # Next.js приложение
    └── src/
        ├── app/                          # роуты App Router
        ├── features/                     # auth, formulations, predictions, simulations, knowledge, audit, reviews
        ├── entities/                     # formulation, prediction, simulation, knowledge-document, model
        ├── shared/                       # api-клиент, ui-кит (shadcn), lib, config
        └── widgets/                      # probability-gauge, rationale-timeline, compare-view...
```

---

## 7. Backend

### 7.1 Доменная модель (Experimento.Domain)

| Сущность | Назначение | Ключевые поля |
|---|---|---|
| `User` | Пользователь-ученый | `Id`, `Email`, `PasswordHash` (BCrypt), `DisplayName`, `Role`, `CreatedAtUtc` |
| `RefreshToken` | Refresh-токены | `Id`, `UserId`, `TokenHash`, `ExpiresAtUtc`, `RevokedAtUtc` |
| `Project` | Рабочее пространство (граница доступа к коммерческой тайне) | `Id`, `Name`, `Description`, `CreatedBy`, `CreatedAtUtc` |
| `Formulation` | Рецептура (агрегат версий) | `Id`, `ProjectId`, `Name`, `TargetPurpose`, `CurrentVersionNumber` |
| `FormulationVersion` | Версия состава | `Id`, `FormulationId`, `VersionNumber`, `Status` (Draft/Submitted/Archived), `Notes`, `CreatedBy`, `CreatedAtUtc` |
| `FormulationComponent` | Компонент версии | `Id`, `VersionId`, `ChemicalName`, `CasNumber?`, `Formula?`, `MolarMass`, `Proportion` (доля 0..1), `Role?` |
| `FormulationConditions` | Условия (owned, 1:1 с версией) | `TemperatureCelsius`, `PressureKPa?`, `PhTarget?`, `Solvent?`, `DeliveryTarget?` |
| `ModelRegistration` | Реестр моделей (F3) | `Id`, `Name`, `Version`, `Description`, `ContextOfUse`, `RegisteredAtUtc` |
| `PredictionJob` | Задача прогноза | `Id`, `VersionId`, `Status` (Pending/Running/Completed/Failed), `Progress`, `Stage`, `RequestedBy`, `Error?`, таймстампы |
| `PredictionResult` | Результат прогноза | `Id`, `JobId`, `ModelRegistrationId`, `SuccessProbability` (0..1), `ToxicityScore`, `StabilityScore`, `SideRiskLevel`, `Summary` |
| `RationaleItem` | Элемент обоснования | `Id`, `ResultId`, `Category` (Stability/Toxicity/Synthesis/Gap), `Claim`, `Explanation`, `Confidence`, `Sources` (jsonb: title, reference, type, similarity) |
| `PredictionReview` | Ревью эксперта (F1) | `Id`, `ResultId`, `ReviewerUserId`, `Decision` (Approved/Rejected/NeedsRevision), `Comment`, `CreatedAtUtc` |
| `ExperimentOutcome` | Фактический исход (F2) | `Id`, `ResultId`, `ActualSuccess`, `ActualMetrics` (jsonb), `Notes`, `RecordedBy`, `RecordedAtUtc` |
| `SimulationJob` | Задача стресс-теста | `Id`, `VersionId`, `Config` (jsonb: варьируемые параметры, диапазоны, шаги, итерации, цель), `Status`, `Progress` |
| `SimulationResult` | Итог симуляции | `Id`, `JobId`, `BestCandidateId`, `Summary`, `IterationsExecuted` |
| `SimulationCandidate` | Кандидат из прогона | `Id`, `ResultId`, `Parameters` (jsonb), `SuccessProbability`, `Score`, `Rank` |
| `KnowledgeDocument` | Документ базы знаний | `Id`, `ProjectId?`, `Title`, `SourceType` (Patent/Paper/InternalExperiment), `Reference`, `Status`, `UploadedBy` |
| `KnowledgeChunk` | Чанк документа + эмбеддинг | `Id`, `DocumentId`, `ChunkIndex`, `Content`, `Embedding` (vector), `Metadata` (jsonb) |
| `AuditEntry` | Append-only запись аудита | `Id` (long, identity), `TimestampUtc`, `ActorUserId?`, `Action`, `EntityType`, `EntityId`, `Payload` (jsonb), `PayloadHash`, `PreviousHash`, `EntryHash` |

Доменные правила:
- `FormulationVersion.EnsureValid()`: сумма пропорций в [0.999, 1.001]; молярные массы > 0.
- `Formulation.NextVersionNumber()` — инкремент, уникальность `(FormulationId, VersionNumber)`.
- `AuditEntry.Create(..., previousHash)` — детерминированный `EntryHash = SHA256(Id|Timestamp|Actor|Action|EntityType|EntityId|PayloadHash|PreviousHash)`.
- `PredictionResult.CalibrationError(outcome)` — модуль разницы прогноза и факта (для статистики F2).

### 7.2 Application (порты и use-cases)

Интерфейсы-порты (реализации в Infrastructure/Ai):
- `IAppDbContext`, `IAuditTrail` (`AppendAsync`, `VerifyChainAsync`)
- `IAuthService` (регистрация/логин/refresh/logout на BCrypt)
- `IPropertyPredictor` — `Task<PredictionOutcome> PredictAsync(FormulationSnapshot, CancellationToken)` — абстракция модели прогнозирования (сейчас эвристика, позже реальная модель).
- `IEmbeddingService`, `IRationaleGenerator`, `IVectorSearchService`, `IJobNotifier`

Use-cases (команды/запросы + обработчики + FluentValidation):
- Auth: `Register`, `Login`, `Refresh`, `Logout`, `GetCurrentUser`
- Projects: CRUD
- Formulations: `CreateFormulation`, `CreateVersion`, `ListVersions`, `GetVersionDetails`, `CompareVersions` (F5)
- Predictions: `SubmitPrediction`, `GetJobStatus`, `GetPredictionResult`, `SubmitReview` (F1), `RecordOutcome` (F2), `GetCalibrationStats` (F2)
- Simulations: `SubmitSimulation`, `GetSimulationStatus`, `GetSimulationResult`
- Knowledge: `UploadDocument`, `ListDocuments`, `SemanticSearch`
- Models: `ListModels` (F3)
- Reports: `GenerateVersionReport` (F4)
- Audit: `GetAuditTrail`, `VerifyAuditIntegrity`

Контракты очередей (MassTransit):
- `SubmitPredictionCommand { JobId }`, `SubmitSimulationCommand { JobId }`, `IngestDocumentCommand { DocumentId }`
- События прогресса/завершения/ошибок для прогнозов и симуляций.

### 7.3 Infrastructure

- **EF Core 10 + Npgsql + Pgvector.EntityFrameworkCore.** Миграции; HNSW-индекс на `KnowledgeChunk.Embedding`; jsonb-колонки. Сид: регистрация модели `rule-based-v1` (F3).
- **AuditTrailRepository**: только `AppendAsync`; `VerifyChainAsync` пересчитывает хеши и находит первую точку разрыва.
- **AuthService**: BCrypt.Net-Next, JWT (access 15 мин), refresh-токены (hash) в httpOnly Secure SameSite=Strict cookie.
- **MassTransit + RabbitMQ**: consumer'ы `PredictionConsumer`, `SimulationConsumer`, `DocumentIngestionConsumer`; retry; идемпотентность по `JobId`.
- **RuleBasedPropertyPredictor** (`IPropertyPredictor`, модель `rule-based-v1`): детерминированный эвристический скоринг — молярный баланс, дисперсия пропорций, температурная устойчивость (встроенная справочная таблица порогов), токсикофоры, штраф за отсутствие стабилизатора при таргетной доставке. Выход: метрики + факторы (вклад каждого правила) — основа rationale.
- **SimulationEngine**: сетка/Monte-Carlo по конфигу с фиксированным сидом (воспроизводимость), каждый кандидат через `IPropertyPredictor`, ранжирование по целевой метрике, прогресс в SignalR, топ-кандидаты + лучший + сводка влияния вариаций.
- **ChunkingService**: чанки ~800 токенов с перекрытием.
- **ReportGenerator** (F4): сборка Markdown/HTML отчета по версии формулы из существующих данных.

### 7.4 Ai (Semantic Kernel)

- `AiProviderFactory`: по секции `Ai:Provider` конфигурации (`openai` | `azureopenai` | `ollama` | `none`) регистрирует `IChatCompletionService` и `ITextEmbeddingGenerationService`. При `none` система деградирует изящно: прогнозы работают (эвристика), в rationale помечается «LLM not configured».
- `RationaleGenerator`: SK `ChatCompletionAgent` + плагины:
  - `KnowledgeSearchPlugin` — топ-K векторный поиск (чанки с ссылками и similarity → claim-level атрибуция, боль #1);
  - `FormulationContextPlugin` — состав, условия, факторы скоринга;
  - `HistoryPlugin` — прошлые прогоны, ревью-решения (F1) и фактические исходы (F2) этой формулы.
  - Выход — структурированный список `RationaleItem` (function calling / JSON schema), каждый пункт со ссылками.
- `EmbeddingService` на базе `ITextEmbeddingGenerationService` (размерность из конфига, дефолт 1536).

### 7.5 WebApi

Контроллеры (все под JWT, кроме register/login/refresh):
- `POST /api/auth/register`, `POST /api/auth/login`, `POST /api/auth/refresh`, `POST /api/auth/logout`, `GET /api/auth/me`
- `GET|POST /api/projects`, `GET|PUT|DELETE /api/projects/{id}`
- `GET|POST /api/projects/{projectId}/formulations`, `GET /api/formulations/{id}`, `POST /api/formulations/{id}/versions`, `GET /api/formulations/{id}/versions`, `GET /api/formulations/{id}/compare` (F5)
- `POST /api/formulation-versions/{id}/predictions`, `GET /api/prediction-jobs/{jobId}`, `GET /api/prediction-jobs/{jobId}/result`
- `POST /api/prediction-results/{id}/review` (F1), `POST /api/prediction-results/{id}/outcome` (F2), `GET /api/calibration` (F2)
- `POST /api/formulation-versions/{id}/simulations`, `GET /api/simulation-jobs/{jobId}`, `GET /api/simulation-jobs/{jobId}/result`
- `POST /api/knowledge/documents`, `GET /api/knowledge/documents`, `POST /api/knowledge/search`
- `GET /api/models` (F3)
- `GET /api/formulation-versions/{id}/report` (F4, скачивание)
- `GET /api/audit`, `GET /api/audit/verify`

Прочее:
- SignalR hub `/hubs/jobs` — группы по `jobId`, события `progress`, `completed`, `faulted`.
- Swagger: `Microsoft.AspNetCore.OpenApi` + Scalar UI; `GET /api/openapi/v1.json` для кодогенерации фронтенда.
- Middleware: ProblemDetails, аудит-фильтр (мутирующие операции → `IAuditTrail`).
- Health checks `/health` (БД, RabbitMQ).
- Секреты только из environment variables / user-secrets.

---

## 8. Frontend (Next.js)

**Стек:** Next.js 15 App Router, TypeScript strict, Tailwind CSS v4, shadcn/ui, шрифт **Manrope**, Recharts, TanStack Query v5, React Hook Form + Zod, openapi-fetch + типы из `openapi-typescript` (генерация из Swagger бэкенда), `@microsoft/signalr`, Vitest + Testing Library.

**Страницы (`src/app`):**
- `/login`, `/register` — формы с валидацией, error/loading состояния.
- `/` — дашборд: последние формулы, активные задачи, сводка прогнозов, **калибровочная статистика «прогноз vs реальность» (F2)**.
- `/formulations` — список; `/formulations/new` — конструктор: компоненты (название, CAS, формула, молярная масса, пропорция с live-проверкой суммы = 100%), условия, цель.
- `/formulations/[id]` — версии, **сравнение версий бок-о-бок (F5)**, действия (новый прогноз, стресс-тест, отчет).
- `/formulations/[id]/versions/[versionId]` — детали версии + запуск прогноза/симуляции + кнопка «Скачать регуляторный отчет» (F4).
- `/predictions/[jobId]` — gauge вероятности успеха, радар/бары метрик, **rationale timeline** с карточками источников, live-прогресс через SignalR, **панель ревью: утвердить/отклонить/на доработку + комментарий (F1)**, форма занесения фактического исхода (F2).
- `/simulations/new` — конфиг вариаций; `/simulations/[jobId]` — scatter/heatmap «параметр → вероятность», подсветка лучшего кандидата, таблица топ-N.
- `/knowledge` — загрузка документов, семантический поиск с выдачей чанков и ссылками.
- `/models` — реестр моделей с context of use (F3).
- `/audit` — лента append-only записей + бейдж целостности (`/api/audit/verify`).

**UX-требования:** единый визуальный стиль (дизайн-токены в `shared/ui`), обязательные loading/error/optimistic состояния, санитизация пользовательского ввода.

---

## 9. Инфраструктура и конфигурация

`docker-compose.yml`:
- `postgres`: `pgvector/pgvector:pg17`, порт 5432, volume.
- `rabbitmq`: `rabbitmq:3-management`, порты 5672/15672.

`appsettings` + env vars: строки подключения, `Jwt:Key`, `Ai:Provider`, `Ai:OpenAi:ApiKey` и т.п. — **только через переменные окружения**. Локальный контур по умолчанию (боль #4).

---

## 10. Тестирование

- **Experimento.Domain.Tests**: версионирование, валидация пропорций, детерминированность хеш-цепочки аудита, расчет ошибки калибровки.
- **Experimento.Application.Tests**: обработчики команд (AAA, NSubstitute) — submit prediction/simulation, ревью (F1), исход эксперимента (F2), аудит-запись.
- **Experimento.Infrastructure.Tests**: детерминированность `RuleBasedPropertyPredictor`, `SimulationEngine` (воспроизводимость по сиду), `AuditTrailRepository.VerifyChainAsync` (детекция подмены), `ReportGenerator` (состав отчета).
- **Frontend**: Vitest + Testing Library — логика формы конструктора (сумма пропорций), отображение вероятности, рендер rationale, форма ревью.
- Команды: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`, `npm run lint`, `npm run test`, `npm run build`.

---

## 11. Пошаговый план реализации

**Фаза 0 — Каркас репозитория**
- `git init`, `.gitignore`, `README.md`, `docker-compose.yml`.
- Проверка: `docker compose up -d` поднимает postgres+rabbitmq.

**Фаза 1 — Решение .NET и домен**
- `backend/Experimento.sln`, проекты `Experimento.Domain`, `Experimento.Application`.
- Все сущности из 7.1 (включая F1–F3), доменные правила, контракты очередей, порты, DTO, валидаторы.
- Проверка: `dotnet build`.

**Фаза 2 — Infrastructure: данные, аудит, auth**
- `Experimento.Infrastructure`: `AppDbContext`, миграции (+ сид модели `rule-based-v1`), `AuditTrailRepository`, `AuthService`, `RuleBasedPropertyPredictor`, `SimulationEngine`, `ChunkingService`, `ReportGenerator` (F4).
- Проверка: `dotnet build` + unit-тесты инфраструктуры.

**Фаза 3 — AI-модуль**
- `Experimento.Ai`: `AiProviderFactory` (openai/azureopenai/ollama/none), `EmbeddingService`, `VectorSearchService`, `RationaleGenerator` с плагинами (включая историю ревью и исходов).
- Проверка: сборка; старт с `Ai:Provider=none` без ключей.

**Фаза 4 — WebApi**
- Контроллеры по 7.5 (включая F1–F5 эндпоинты), SignalR hub, Swagger/Scalar, ProblemDetails, аудит-фильтр, health checks, DI-композиция, MassTransit + consumer'ы.
- Проверка: `dotnet run`, Swagger отвечает, регистрация → логин → проект/формула через curl.

**Фаза 5 — Тесты бэкенда**
- Три тестовых проекта по разделу 10. Проверка: `dotnet test` зеленый.

**Фаза 6 — Каркас фронтенда**
- `frontend/`: Next.js 15, TS strict, Tailwind v4, shadcn/ui, Manrope, ESLint/Prettier.
- Генерация API-клиента (`npm run generate:api`), обертка openapi-fetch, TanStack Query, SignalR-клиент.
- Проверка: `npm run build`.

**Фаза 7 — Функциональные страницы фронтенда**
- Auth → дашборд (с калибровкой) → конструктор → версии (сравнение) → прогноз (метрики, rationale, ревью, исход) → симуляции → база знаний → реестр моделей → аудит.
- Проверка: `npm run lint`, `npm run test`, ручной сценарий в браузере.

**Фаза 8 — Финальная интеграция и проверка**
- Полный сценарий: регистрация → проект → формула → прогноз (очередь, прогресс, метрики, rationale) → ревью → стресс-тест → занесение фактического исхода → скачивание регуляторного отчета → проверка аудита.
- `dotnet format --verify-no-changes`, все тесты, README с инструкцией запуска.

---

## 12. Допущения и решения

1. **Прогнозная модель — детерминированная эвристика** за портом `IPropertyPredictor`, зарегистрированная в реестре как `rule-based-v1`. Реальная ML-модель подключается позже без изменения архитектуры.
2. **LLM-провайдер абстрагирован**: конфигурация `openai|azureopenai|ollama|none`; без ключей система работоспособна.
3. **База знаний пополняется пользователем** (загрузка статей/патентов); внешнего подключения к патентным базам нет.
4. **Размерность эмбеддинга** — параметр конфигурации (дефолт 1536).
5. **Аудит** — хеш-цепочка SHA-256 в БД; внешние HSM/WORM не используются.
6. **Авторизация** — упрощенная (создатель проекта имеет доступ); командные роли вне рамок.
7. Локальный запуск предполагает установленный Docker.
8. Идентификаторы, схемы БД, код — только английский; коммуникация и план — русский.
9. Отчет (F4) генерируется в Markdown/HTML без сторонних PDF-библиотек (простота).

---

## 13. Проверка результата (итоговая)

| Требование | Как проверяется |
|---|---|
| Ввод эксперимента | Конструктор сохраняет компоненты/пропорции/условия; видно в API |
| Прогноз вероятности и свойств | Результат содержит все метрики; расчет асинхронный, прогресс в UI |
| Стресс-тест | Лучший кандидат + сводка влияния вариаций |
| Rationale Tracking | Каждый пункт обоснования имеет объяснение и ссылки на источники/прошлые прогоны |
| Data Integrity | `/api/audit/verify` подтверждает целостность; записи только добавляются |
| Human-in-the-loop (F1) | Ревью сохраняется и видно в аудите и истории |
| Feedback loop (F2) | Исход сохраняется, калибровочная статистика на дашборде |
| Реестр моделей (F3) | Каждый результат ссылается на версию модели; страница `/models` |
| Регуляторный отчет (F4) | Скачанный отчет содержит состав, прогнозы, rationale, симуляции, аудит |
| Сравнение версий (F5) | Дифф компонентов и метрик отображается |
| Качество | `dotnet test`, `dotnet format --verify-no-changes`, `npm run lint`, `npm run test`, `npm run build` — зеленые |

---

## 14. Источники исследования рынка

- [1] [Role of AI and ML in Pharmaceutical Formulation: Recent Advances, Challenges and Future Prospectus (Biotech Asia, 2026)](https://www.biotech-asia.org/download/58762/) — black-box эффект, объяснимость, сокращение физических испытаний до ~50%.
- [2] [AI in Pharmaceutical Formulation and Dosage Calculations (Pharmaceutics/PMC, 2025)](https://pmc.ncbi.nlm.nih.gov/articles/PMC12655709/) — качество данных, интерпретируемость, регуляторное принятие, разрыв компетенций.
- [3] [Integrating AI into drug delivery systems: formulation development and current challenges (Eur J Pharm Biopharm, 2026)](https://pubmed.ncbi.nlm.nih.gov/42242512/) — trial-and-error ресурсоемок, ИИ как decision-support, QbD.
- [4] [The Confidentiality Barrier to AI-Enabled Drug Development (The Medicine Maker, 2026)](https://themedicinemaker.com/issues/2026/articles/july/the-confidentiality-barrier-to-aienabled-drug-development/) — коммерческая тайна, невозможность загрузки структур в публичные ИИ, data sovereignty.
- [5] [FDA Draft Guidance: Considerations for the Use of AI to Support Regulatory Decision-Making (Jan 2025)](https://mccreadiegroup.com/wp-content/uploads/2026/04/FDA_AI-Guidance59502407dft-1-1.pdf) — risk-based credibility assessment, context of use, документация модели.
- [6] [Why Regulatory Reviewers Reject Black-Box AI Outputs (Pienomial, 2026)](https://www.pienomial.com/blog/why-regulatory-reviewers-reject-black-box-ai-outputs-and-what-documentation-they-actually-want) — claim-level source attribution, human oversight как свойство дизайна, FDA–EMA принципы янв. 2026.
- [7] [What Is Traceable AI? Definition, Standards and Why Pharma Needs It Now (Pienomial, 2026)](https://www.pienomial.com/blog/what-is-traceable-ai-definition-standards-and-why-pharma-needs-it-now) — 10 принципов FDA–EMA, traceability, ALCOA++.
- [8] [How to Reduce Lab Data Silos with a Unified LIMS (Sapio Sciences)](https://www.sapiosciences.com/resource/how-to-reduce-lab-data-silos-with-a-unified-lims/) + [Escaping the Paper-on-Glass Trap (Lab Manager, 2026)](https://www.labmanager.com/escaping-the-paper-on-glass-trap-data-standardization-is-a-strategic-imperative-for-ai-35363) — data silos, машиночитаемые данные, FAIR.
- Дополнительно: [FDA & EMA Good AI Practice Guide summary (IntuitionLabs)](https://intuitionlabs.ai/pdfs/fda-ema-good-ai-practice-guide-for-drug-development.pdf), [Best AI Drug Discovery Companies 2026](https://artificialintelligencecompanies.com/best/ai-drug-discovery-companies/), [Schrödinger LiveDesign](https://www.schrodinger.com/platform/products/livedesign/).
