# CreateSekibanDcbTemplate

A `dotnet` tool (distributed via `dnx`) that installs a Sekiban template
package and generates a new project from it. It prompts for a project name
and a template type, runs `dotnet new install` followed by `dotnet new
<template-short-name> -n <ProjectName>`, and prints the tool's exit status.

## Usage

```bash
dnx CreateSekibanDcbTemplate [ProjectName] [Options]
# or, from a local checkout:
dotnet run --project tools/dotnettools/CreateSekibanDcbTemplate -- [ProjectName] [Options]
```

| Option | Effect |
| --- | --- |
| `[ProjectName]` | Project name to generate. Prompted (default `Contoso.Dcb`) when omitted. |
| `-t, --type <type>` | Template type. Prompted (default `decider`) when omitted. |
| `-h, --help` | Show help. |

## Template types

| Type | Package | Generated template |
| --- | --- | --- |
| `decider` (recommended) | `Sekiban.Dcb.Templates` | `sekiban-dcb-decider` |
| `dcb` | `Sekiban.Dcb.Templates` | `sekiban-dcb-orleans` |
| `withoutresult` | `Sekiban.Dcb.Templates` | `sekiban-dcb-orleans-withoutresult` |
| `pure` | `Sekiban.Pure.Templates` | `sekiban-orleans-aspire` |
| `decider-aws` | `Sekiban.Dcb.Templates` | `sekiban-dcb-decider-aws` |
| `withoutresult-aws` | `Sekiban.Dcb.Templates` | `sekiban-dcb-orleans-aws` |
| `wasm-decider` | `Sekiban.Dcb.WasmRuntime.Templates` | `sekiban-wasm-decider` |

Every type installs its package via `dotnet new install <package>` and then
runs `dotnet new <short-name> -n <ProjectName>`.

## `wasm-decider`

Generates an Aspire solution that hosts the **public Sekiban WASM runtime
container** (`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host`) with a
Postgres event store: a Decider-pattern domain, a NativeAOT-LLVM `wasi-wasm`
projector module, and an Aspire AppHost wiring the container + Postgres
through the `Sekiban.Dcb.WasmRuntime.Aspire` package. The package and
template are maintained in the
[SekibanWasmRuntime](https://github.com/J-Tech-Japan/SekibanWasmRuntime)
repository (SWR-G068); see its
[`docs/templates/sekiban-wasm-decider.md`](https://github.com/J-Tech-Japan/SekibanWasmRuntime/blob/main/docs/templates/sekiban-wasm-decider.md)
for the template's own generation options (e.g. `--IncludeTests`).

```bash
dnx CreateSekibanDcbTemplate MyWeather -t wasm-decider
```

On success the tool prints the generated project's next steps:

```bash
cd MyWeather
bash scripts/build-wasm.sh          # builds the WASM module + runtime manifest
dotnet restore
dotnet build
dotnet run --project MyWeather.AppHost   # starts Postgres + the public runtime container
# or run the end-to-end smoke instead (starts the AppHost itself):
bash scripts/smoke.sh
```

### If `Sekiban.Dcb.WasmRuntime.Templates` is not yet published

`Sekiban.Dcb.WasmRuntime.Templates` is published from the
SekibanWasmRuntime repository's own `templates-v*` release lane
(independent of this tool's release cadence). If the `dotnet new install`
step runs before that package has published, it fails with a NuGet
not-found error (`dotnet new` exit code 103); the tool reports this clearly,
names the package, and points at the SekibanWasmRuntime repository, without
touching any other installed templates:

```
❌ dotnet new install Sekiban.Dcb.WasmRuntime.Templates failed with exit code 103.

The Sekiban.Dcb.WasmRuntime.Templates package could not be installed.
It is published from the SekibanWasmRuntime repository's templates-v* release lane;
if it has not been published to NuGet.org yet, this install will fail with a not-found error.
See https://github.com/J-Tech-Japan/SekibanWasmRuntime for the release status, or install a
locally packed nupkg with: dotnet new install <path-to-Sekiban.Dcb.WasmRuntime.Templates.nupkg>
```

To verify the `wasm-decider` flow before that publish happens, install a
locally packed nupkg first (pack it from a SekibanWasmRuntime checkout, e.g.
`dotnet pack templates/Sekiban.Dcb.WasmRuntime.Templates/...` plus its
`Sekiban.Dcb.WasmRuntime.Aspire` dependency), and point a `NuGet.config` in
the project's working directory at that local output directory (with
`nuget.org` as a fallback source) before running the tool -- the tool's own
`dotnet new install Sekiban.Dcb.WasmRuntime.Templates` then resolves from
the local source instead of the registry. This was verified locally: with
the packages packed and a local `NuGet.config` in place, the tool installed
the template, generated a project named `WasmDeciderSmoke` with the
expected layout (`*.AppHost`, `*.Domain`, `*.Domain.Tests`, `*.Wasm`,
`scripts/`, `README.md`), and printed the next-step commands above
unchanged.
