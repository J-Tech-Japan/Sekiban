# SEK-G126 fix round 1 evidence

Validated locally on 2026-10-07 UTC, from branch `sek-g126-templates` based on `95a879e1`. Changes are confined to the template package, the English/Japanese template release bodies, and the explicitly authorized currency fixture. No library code changed; no repository push, tag, workflow dispatch, or publication was performed.

## Per-template results

All five generated applications passed the core student/classroom/enrollment flow, duplicate/rejected commands, malformed JSON, materialized-view student/classroom/enrollment/status routes, weather and unsafe-window MV reads, Development debug/maintenance exposure, and Production exposure checks. Timings below measure the list request immediately after its write response, with `waitForSortableUniqueId`; every response contained the newly written item, with an assertion requiring elapsed time below two seconds.

| Template | Unit tests | Student list | Reservation list | Core/MV + Dev/Prod | CLI | .NET images | Next.js build/gates |
| --- | ---: | ---: | ---: | --- | --- | --- | --- |
| Orleans (base) | 16 passed | 1.014 s | N/A | Passed | `status` passed | 2/2 built | N/A |
| WithoutResult | 16 passed | 0.821 s | N/A | Passed | `status` passed | 2/2 built | N/A |
| WithoutResult.Aws | 16 passed | 1.023 s | N/A | Passed | `list` passed | 2/2 built | N/A |
| Decider | 256 passed | 0.206 s | 0.209 s | Passed | `status` passed | 2/2 built | Passed |
| Decider.Aws | 248 passed | 0.205 s | 0.212 s | Passed | `list` passed | 2/2 built | Passed |

The AWS templates intentionally ship a list-only CLI, without a status command. Both AWS applications ran against token-free LocalStack 4.9, using DynamoDB/S3 and a separate PostgreSQL materialized-view database. Decider also used a separate Identity database.

## Configuration and HTTP checks

- Both Deciders rejected Production startup with a missing or short JWT key even when Identity was absent. A valid key with missing Identity configuration failed explicitly with an Identity diagnostic. With Identity configured but no JWT key, startup failed with `Jwt:SecretKey`. With both configured, Production started and authenticated the configured initial administrator through bearer and cookie login; sample administrator login returned 401.
- Development bearer login, cookie login, `/auth/me`, cookie logout, reservation creation and reservation conflict checks passed in both Deciders. Duplicate enrollment, duplicate student/classroom creation and repeated enrollment drop returned 400. Malformed bodies returned 400 in every application, including the exact body `{"studentId":"x"` in Production.
- Production returned 404 for `/api/debug/events` and all five projection maintenance routes: persist, deactivate, refresh, snapshot and overwrite-version. Test-data generation was unavailable, and any-origin CORS was absent. Development exposed its intended routes.
- Both Next.js applications passed `npm run build`. Real HTTP `/login` responses showed quick-login controls in Development and hid them in Production; Production `testData.generate` tRPC requests returned 404. Development validation used `WATCHPACK_POLLING=true` after the local file watcher hit its open-file limit.

## Stream and grain-key audit

Traced all five API registrations of `OrleansEventPublisher`, destination and subscription resolvers, both Decider realtime routers, and every direct stream access. Each publisher's destination resolver receives the registered `IServiceIdProvider`. The subscription resolver parses the service-scoped projection key and selects the same `<serviceId>|AllEvents` namespace. Both realtime routers now build a scoped ReservationProjector key and use only the resolved stream descriptor for direct `GetStream` access. Unit tests compare provider, namespace and stream id for two distinct service ids.

Audited all 57 direct `GetGrain<...>` calls: base 11, WithoutResult 11, WithoutResult.Aws 11, Decider 12 and Decider.Aws 12. Checked weather normal/generic/single statistics and status routes, five maintenance routes, and Decider authentication initialization. Every application projection key uses `ServiceIdGrainKey.Build`; the only numeric literal keys are the two Orleans system `IManagementGrain(0)` calls. The base generic weather statistics/status calls are now scoped.

## Containers

All ten final .NET stages use `USER $APP_UID` and port 8080. API images give that user ownership of the `/app` directory so the supported SQLite backend can create its `events.db`; copied application binaries retain their existing ownership. A final AWS API image ran as `uid=1654(app) gid=1654(app)` and served `/health` on container port 8080. The final base API image also ran as UID 1654, logged `Now listening on: http://[::]:8080`, accepted a student write with SQLite, and created `/app/events.db` owned by UID 1654.

## Reproducing the HTTP checks

Generate an isolated application named `TemplateApp`, build it, and start its API with local services. `runtime-smoke.py` uses the same sample Development credentials as the generated application. Set the configured service id in the CLI environment as well as the API when overriding it.

```sh
TEMPLATE_APP_DIR=/absolute/path/to/TemplateApp \
TEMPLATE_EVENT_STORE_CONNECTION='Host=...;Database=DcbPostgres;...' \
python3 templates/Sekiban.Dcb.Templates/validation/runtime-smoke.py PORT TEMPLATE_KIND
```

For AWS, omit the event-store connection variable; the script exercises the shipped list-only CLI. For Production, configure an isolated initial administrator with email `initialadmin@g126.example` and password `Initial1234%`, provide a valid JWT secret and Identity connection, and run:

```sh
python3 templates/Sekiban.Dcb.Templates/validation/production-smoke.py PORT TEMPLATE_KIND
```

`TEMPLATE_KIND` is the full content directory name, such as `Sekiban.Dcb.Orleans.Decider.Aws`. Both scripts exit unsuccessfully on failed checks, and the Development script asserts the two-second delivery limit and presence of the new item.

## Local execution notes

Aspire MCP discovery and resource inspection were attempted while AppHosts were running, but automatic approval review rejected both because the configured approval policy was `never`. AppHost smoke passed base and WithoutResult. An AWS AppHost hit the existing DynamoDB table-creation race; the complete final matrix used directly started generated APIs with isolated PostgreSQL, Azurite and LocalStack services and retried after table initialization. No library workaround was committed. The original Decider AppHost HTTP checks passed, but its final CLI step hit a harness environment-variable error; the corrected, checked-in smoke subsequently passed the full matrix. Docker builds used a writable temporary `BUILDX_CONFIG` because the sandbox could not update Docker's default local activity file.

Raw local evidence is under `/tmp/sek-g126`: `fix-controlled-final.log`, `fix-controlled/*/Development-final-smoke.log`, `fix-controlled/*/Production-final-smoke.log`, `fix-tests.log`, `fix-docker.log`, `fix-exposure.log`, `fix-auth-start.log`, `fix-ui-gates-final.log`, `fix-container-run-final.log`, and `fix-sqlite-container.log`.

## Public-consumer fixture

The final public-consumer path completed with exit code 0 and `Template packaged-consumer validation and kept killing fixtures passed.` The currency mutant now uses public version `10.23.0`: restore succeeded, Release build succeeded, and validation rejected it specifically because the version props must declare the authority version `10.23.1`. The build requirement remains intact, and the script now checks the currency diagnostic explicitly. The runbook instructs maintainers to raise this compatible stale version when templates adopt a newer API. The separate legacy composition fixture remains on its intentionally incompatible `10.8.2` graph.

```sh
bash dcb/tests/Sekiban.Dcb.TemplateValidation/run-packaged-consumer.sh
```

Evidence: `/tmp/sek-g126/fix-public-consumer-final.log`. The final run used public NuGet packages, without a local library feed. Both current and legacy status-composition checks completed with their expected outcomes. README/release inclusion checks, Bash/Python syntax checks and `git diff --check` also passed.
