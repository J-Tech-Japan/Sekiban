# DcbOrleans.Benchmark Playwright E2E

This package provides heavy E2E coverage for the DCB benchmark UI and API surface.

## Covered scenario

- Starts `DcbOrleans.AppHost` from Playwright `webServer`
- Executes benchmark runs via UI:
  - 10,000 records on `standard` projection mode
  - 20,000 records on `generic` projection mode
- Verifies projection counts and catch-up state for:
  - `standard`
  - `single`
  - `generic`
- Executes projection control cycle (`persist` -> `deactivate` -> `refresh`) and validates recovery
- Verifies cold-event endpoints (`status`, `progress`, `catalog`, `export`)

## Prerequisites

- Docker Desktop running (Aspire dependencies: PostgreSQL/Azurite)
- .NET SDK matching repository requirements
- Node.js 20+

## Run

```bash
cd dcb/internalUsages/DcbOrleans.Benchmark.Playwright
npm install
npm run install:browsers
npm test
```

## Optional environment variables

- `BENCH_BASE_URL` (default: `http://127.0.0.1:5411`)
- `DCB_APPHOST_PROJECT_PATH` (default: `../DcbOrleans.AppHost/DcbOrleans.AppHost.csproj`)
- `BENCH_HTTP_PORT` (used by AppHost, auto-derived from `BENCH_BASE_URL` if omitted)
- `PW_ASSERT_SINGLE_PROJECTION` (default: `false`, set `true` to fail if `mode=single` status endpoint is not OK)
- `BENCH_API_TIMEOUT_SECONDS` (default: `15`, benchmark proxy API timeout)
- `BENCH_COLD_EXPORT_TIMEOUT_SECONDS` (default: `120`, timeout only for cold export endpoint)
