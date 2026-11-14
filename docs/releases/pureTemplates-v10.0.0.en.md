# Sekiban.Pure.Templates 10.0.0

Sekiban.Pure.Templates 10.0.0 moves the two Sekiban.Pure project templates, `sekiban-orleans-aspire` and `sekiban-dapr-aspire`, to .NET 10 and Sekiban.Pure 10.0.0. It replaces 1.1.5, which generated `net9.0` projects that referenced the preview package `1.0.1-preview21`. **Sekiban.Pure is in maintenance mode (security fixes only). For new projects we recommend Sekiban DCB.**

## Install

```
dotnet new install Sekiban.Pure.Templates::10.0.0
dotnet new sekiban-orleans-aspire -n MyApp
dotnet new sekiban-dapr-aspire -n MyApp
```

You need the .NET 10 SDK. Projects you generated earlier are not changed.

## What changed

- **Target framework and packages:** generated projects target `net10.0` and reference `Sekiban.Pure*` 10.0.0.
- **Dependencies:** they moved with the framework so that the generated apps restore without known-vulnerability warnings at the time of this release: Orleans 10.4.2, .NET Aspire 13.6.5, OpenTelemetry 1.16.0, Scalar.AspNetCore 2.10.3, and Microsoft.AspNetCore.OpenApi and Microsoft.Extensions.Logging.Abstractions 10.0.15.
- **Obsolete calls:** the Orleans API service no longer calls the obsolete Azure client registration methods.
- **Deployment files:** the Dockerfiles, deployment scripts and Bicep files name .NET 10.
- **READMEs:** the Orleans configuration section of the package README now matches the AppHost of the template.

## What was checked

- In CI, both templates are generated from the packed package and built with known-vulnerability warnings (NU1900 to NU1905) treated as errors, and their unit tests pass.
- The Orleans app generated from the template was started with Docker (Aspire starts PostgreSQL and Azure Storage containers). A command was accepted, the new forecast was read back through the query API, and the count query, the Blazor page and the health endpoint answered with 200.

## What was not checked

- **Dapr.** The Dapr template restores, builds and passes its unit tests, but it has not been run end to end against a Dapr runtime (placement service, scheduler and sidecars) on these versions.
- **.NET 11 SDK.** The generated projects target `net10.0` and were built and tested with the .NET 10 SDK only. They were not built with the .NET 11 SDK; in our one attempt with 11.0.100, even a stock `net10.0` Blazor app did not build.
- **Deployment files and browser tests.** The Bicep files, the deployment scripts and the Dockerfiles were not executed, and the Playwright tests of the Orleans template were not run.

## Package

`Sekiban.Pure.Templates` 10.0.0, with the templates `sekiban-orleans-aspire` and `sekiban-dapr-aspire`.
