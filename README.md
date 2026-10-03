# Experimento — AI-Driven Formulation & R&D Co-Pilot

Intelligent platform for chemists, biologists, and pharmaceutical scientists: design formulations, predict synthesis success, toxicity and stability, run stress-test simulations, get traceable rationale, and keep an immutable audit trail.

## Stack

- **Backend**: .NET 10 / C#, Clean Architecture (Domain / Application / Infrastructure / Ai / WebApi)
- **Queues**: MassTransit + RabbitMQ (async heavy computations)
- **Database**: PostgreSQL 17 + pgvector (relational data + vector search)
- **AI**: Microsoft Semantic Kernel, provider-agnostic (OpenAI / Azure OpenAI / Ollama / none)
- **Frontend**: Next.js (App Router) + TypeScript strict + Tailwind + shadcn/ui + Recharts
- **Auth**: JWT access token + refresh token in httpOnly cookie, BCrypt passwords

## Prerequisites

- Docker (PostgreSQL + RabbitMQ run via docker-compose)
- .NET 10 SDK
- Node.js 20+

## Quick start

```bash
# 1. Infrastructure
docker compose up -d

# 2. Backend
cd backend
dotnet run --project src/Experimento.WebApi

# 3. Frontend
cd frontend
npm install
npm run dev
```

- API: http://localhost:5080 (Swagger UI included)
- Frontend: http://localhost:3000

## Configuration

All secrets and connection settings come from environment variables (see `backend/src/Experimento.WebApi/appsettings.json` for keys). Never commit secrets.

Key settings:

| Setting | Description |
|---|---|
| `ConnectionStrings__Default` | PostgreSQL connection string |
| `RabbitMq__Host` / `RabbitMq__Username` / `RabbitMq__Password` | RabbitMQ connection |
| `Jwt__Key` | JWT signing key (min 32 chars) |
| `Ai__Provider` | `openai` \| `azureopenai` \| `ollama` \| `none` |
| `Ai__OpenAi__ApiKey` | OpenAI API key (only when provider=openai) |

With `Ai__Provider=none` the system runs fully offline: heuristic predictions work, rationale is marked "LLM not configured".

## Tests

```bash
(cd backend && dotnet test)
(cd frontend && npm run lint && npx tsc --noEmit)
```

For browser smoke tests, set a unique `JWT_KEY` (at least 32 characters) in `.env`.
The test stack seeds a known test account and uses fixed container names and ports, so
stop any regular Experimento stack before starting it. Run these commands from the
repository root; the separate Compose project keeps test data in isolated volumes:

```bash
docker compose -p experimento-ci -f docker-compose.yml -f .github/compose.ci.yml up -d --build
docker compose -p experimento-ci -f docker-compose.yml -f .github/compose.ci.yml exec -T postgres psql -v ON_ERROR_STOP=1 -U experimento -d experimento < .github/ci/seed-catalog.sql
(cd frontend && npm ci && npx playwright install chromium && npm run test:e2e:ci)
```

After the tests, remove only the isolated test stack and its volumes:

```bash
docker compose -p experimento-ci -f docker-compose.yml -f .github/compose.ci.yml down -v
```

`npm run test:e2e` runs the full browser suite, including scenarios that require
external services. CI runs only the `@ci` subset with a seeded local catalog.
