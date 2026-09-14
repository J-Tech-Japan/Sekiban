Closes #1234

## Scope
- Moves all five template authorities and README to DCB 10.22.0 while retaining Orleans 10.3.1.
- Adds exact 26-project/package manifest and nupkg dependency inspection, including PostgreSQL net9/net10 closure and Azure-free Orleans Core.
- Adds isolated local-feed pack/consumer validation for five generated templates, staged release-record validator, bilingual reviewed release bodies, and deterministic negative fixtures.
- Hardens library/template workflow ordering, same-commit tag checks, finite public visibility waits, and explicit non-draft reviewed bilingual release bodies. No publication or release was performed by this PR.

## Evidence
- `bash -n dcb/tests/Sekiban.Dcb.TemplateValidation/*.sh`: passed.
- `validate-release-tags.sh --self-test`: passed; exact 26 effective projects.
- `dotnet build dcb/tests/Sekiban.Dcb.TemplateValidation/Sekiban.Dcb.TemplateValidation.csproj -c Release`: passed, 0 warnings/0 errors.
- `TemplateValidation source`, `workflow`, `packages`, and `release-record`: passed.
- Disposable local feed: 26 packages at 10.22.0; package inspection passed for PostgreSQL Relational 9.0.13/10.0.3, Azure Queue dependencies, and Azure-free Orleans Core.
- `run-packaged-consumer.sh --feed <disposable local feed> --version 10.22.0`: passed; five generated outputs, 11 bundled test projects, current and legacy composition controls, release-record valid states, and all named negative mutants.
- PostgreSQL project builds: `-f net9.0` and `-f net10.0` passed.
- `git diff --check`: passed.

The local machine used SDK 11.0.100-preview.3; the source solution built both target frameworks. The mandatory GitHub workflows install the supported .NET 9 and .NET 10 SDKs for CI confirmation. No tags, packages, releases, issue closures, or announcements were created.