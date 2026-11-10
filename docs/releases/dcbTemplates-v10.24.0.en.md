# DCB templates 10.24.0

The five starter templates for Sekiban DCB 10.24.0. Generated projects reference the 10.24.0 packages and Orleans 10.4.2.

## Library changes included

Generated projects get Orleans 10.4.2. Read the 10.24.0 library release notes before you upgrade a running system: the whole Orleans cluster must be upgraded together, some Orleans metric names changed, and existing PostgreSQL databases created with the Orleans scripts need the non-destructive upgrade steps in the deployment guide.

## Template changes

- The templates use current dependency versions: Aspire 13.6.4, OpenTelemetry 1.20, ASP.NET Core, EF Core and Microsoft.Extensions 10.0.14, Microsoft.Extensions.Http.Resilience and ServiceDiscovery 10.11.0, Scalar 2.19.0, and newer patch or minor versions of the other packages.
- A new project builds without known-vulnerability warnings. The earlier versions brought vulnerable MessagePack, Microsoft.OpenApi and OpenTelemetry packages.
- The Azure templates reference `Microsoft.Data.Sqlite` 10.0.14 directly. `Sekiban.Dcb.Sqlite` 10.24.0 depends on 10.0.3, which brings `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 with a known high-severity vulnerability (GHSA-2m69-gcr7-jv3q).
- A new project builds without compiler warnings. The API service no longer calls the deprecated `WithOpenApi()`; the OpenAPI document still describes the endpoints. If a required Event Hub or storage connection setting is missing, the API service now stops at startup with a message that names the setting.
- Decider template: the API service is now reported healthy and both web front ends start. Before, the AppHost checked health on an HTTPS endpoint that the API service does not listen on, so the API service stayed unhealthy and the web front ends waited for it. The Blazor front end is now reachable from the dashboard link.
- The `http` launch profile of every AppHost sets `ASPIRE_ALLOW_UNSECURED_TRANSPORT`, so `dotnet run --launch-profile http` starts.
- The AppHost projects set `AspireUseCliBundle` to `true`, as Aspire 13.6 recommends. `dotnet run` of an AppHost now starts it through the Aspire CLI. If the `aspire` command (13.5.0 or later) is not installed, the build gets the matching Aspire CLI through `dnx`.

## Existing projects

Updating the template package does not change a project you already generated. To take the library changes, set `SekibanDcbVersion` to `10.24.0` and `MicrosoftOrleansVersion` to `10.4.2` in `SekibanDcbTemplateVersion.props`. To fix the vulnerability warnings, update the same packages as above; in a project that references `Sekiban.Dcb.Sqlite`, add a reference to `Microsoft.Data.Sqlite` 10.0.14.
