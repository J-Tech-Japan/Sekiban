# SekibanDcbDecider

Start locally from the solution directory with Docker running:

```bash
dotnet build SekibanDcbDecider.slnx
dotnet run --project SekibanDcbDecider.AppHost --launch-profile http
```

Use the Aspire dashboard for the API, Blazor Web and database endpoints. The AppHost and project launch profiles set `ASPNETCORE_ENVIRONMENT=Development`. Stop the AppHost when finished. The AppHost configures the event store and the separate PostgreSQL MV target; `/api/mv/status`, `/api/mv/students`, `/api/mv/classrooms` and `/api/mv/enrollments` demonstrate the read model. `waitForSortableUniqueId` can wait for a command's position. `/api/weatherforecast-uwmv` demonstrates unsafe-window reads.

## Development settings

Debug event inspection and projection persist, deactivate, refresh, snapshot and overwrite-version routes exist only in Development. Any-origin CORS is limited to Development. Local streams use memory; they do not guarantee durable delivery on restart.

Sample users (including `admin@example.com`, password `Sekiban1234%`) and the fixed JWT signing key are Development-only. Identity tables and roles are initialized in every environment. Registration grants only User. The Blazor Web has no authentication client or login page; use WebNext for registration and login. The AppHost runs WebNext with `next dev`. Its server uses `NODE_ENV` to gate quick login and test-data controls and rejects test-data procedures outside development. Test-data API routes are Development-only and require the Admin role (they create rooms and sample users).

Meeting-room rules: creating and updating rooms, confirming and rejecting reservations, and approval decisions require the Admin role. Only the organizer or an administrator can hold or cancel a reservation. "Administrator" always means the Admin role of the signed-in Identity user (from the token): the API copies it onto the command, so it also decides who is exempt from the booking window and monthly limit. The domain UserAccess roles are kept for display only and are not used for authorization. Holding a draft uses the draft's room; for a room that requires approval the hold starts the approval request itself (an explicit `approvalRequestId` must be a pending request for the same reservation).

`Orleans:UseInMemoryStreams` selects memory streams locally. For deployed Azure Queue or Event Hub streams, supply the existing storage/queue configuration in the infrastructure guides; PubSubStore must be persistent.

## Before production

If the auth database cannot be initialized, `/health` stays unhealthy and the instance must be restarted. The shipped Azure Container Apps definition restarts it through its startup probe; the shipped App Service definitions configure no health probe, and a local run is not restarted.

- Set and keep `Sekiban:ServiceId` stable. Its default is `sekiban-app`; it partitions events, projections, streams and materialized views. Use a distinct identity when applications share a backend. `Orleans:ServiceId` is a separate cluster setting. For a project generated earlier with data under `default`, retain its existing identity.
- Configure durable storage, clustering, stream delivery, snapshot access and allowed CORS origins for your deployment. Test restart recovery and your multi-silo topology.
- Keep debug and maintenance routes disabled. If operators need them, explicitly map them behind your own authenticated operator policy, for example `.RequireAuthorization("Operators")`; define the policy and its permissions before exposing routes.
- Grant only the database and blob permissions required by the configured stores, bootstrap schemas with an appropriate deployment identity, then review runtime schema permissions.
- Supply `Jwt__SecretKey` (at least 32 characters) from a secret store before deployment. Docker images use Production; Azure defaults to Production and AWS uses Staging/Production, so the development key is not loaded. The shipped infrastructure does not wire this secret: use an App Service Key Vault reference, a Container Apps secret reference or an ECS task definition `secrets` entry backed by Secrets Manager. Startup fails without a valid key.
- To create the first administrator outside Development, supply `Auth__InitialAdmin__Email` and `Auth__InitialAdmin__Password` from secrets when no administrator exists. An existing registered account is never promoted. Omit these settings after bootstrap. Build WebNext with `npm install` and `npm run build`; production startup uses `npm start` and `NODE_ENV=production`.
- Configure the bundled CLI with the same `Sekiban:ServiceId` (environment variable `Sekiban__ServiceId` or CLI user secrets) and store connection as the API. Its SQLite cache path includes the service identity and a keyed hash of the connection settings (connection string, database and PG* environment variables; the key lives in `.cache-key` in the cache root), so a cache is only reused for exactly the connection it was synced from; a password change starts a new cache. The CLI user secrets ID is unique to each generated application. Commands exit with code 1 and write the error to stderr on failure; `delete` and `cache-clear` need `--yes` when stdin is not interactive. The CLI can also read the SQLite event store (`-d sqlite --sqlite-path <ApiService>/events.db`).

## Design guidance

Read the upstream guides before changing defaults:

- [Immutable projection inputs](https://github.com/J-Tech-Japan/Sekiban/blob/main/docs/dcb_llm/04_multiple_aggregate_projector.md): return new payloads; use `IMutatesProjectionInput` only as a deliberate optimization. Retryable catch-up and rebuild failures return 503 with `Retry-After`; a query during catch-up waits at most 5 seconds for it (`FirstQueryCatchUpMaxWaitMs = 5000`), and the Orleans activation timeout is 5 minutes for large snapshot restores.
- [Snapshot storage and blob prefixes](https://github.com/J-Tech-Japan/Sekiban/blob/main/docs/dcb_llm/11_storage_providers.md): this template uses a dedicated snapshot container or bucket without a prefix; plan isolation when sharing storage.
- [Orleans and grain directory options](https://github.com/J-Tech-Japan/Sekiban/blob/main/docs/dcb_llm/10_orleans_setup.md): distributed directory prerequisites must be met before enabling it.
- [Fast and strict consistency](https://github.com/J-Tech-Japan/Sekiban/blob/main/docs/dcb_llm/04_multiple_aggregate_projector.md): choose the query guarantee deliberately.
- [Storage and schema permissions](https://github.com/J-Tech-Japan/Sekiban/blob/main/docs/dcb_llm/11_storage_providers.md) and [materialized views](https://github.com/J-Tech-Japan/Sekiban/blob/main/docs/dcb_llm/20_materialized_view.md).
