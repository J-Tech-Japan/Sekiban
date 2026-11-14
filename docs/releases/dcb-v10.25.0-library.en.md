# Sekiban DCB 10.25.0

- Version: `10.25.0`. 26 DCB library packages. Targets net10.0 and net11.0.
- .NET 9 support is removed: the packages no longer contain net9.0 assets. .NET 9 reached end of support in November 2026. Projects that still target net9.0 must stay on 10.24.x.
- The packages now include net11.0 assets. The net10.0 and net11.0 assets use the same dependency versions as 10.24.3 (EF Core, Npgsql and Microsoft.Extensions 10.x, Orleans 10.4.2); no .NET 11 package is required.
- The public API and the behaviour are unchanged since 10.24.3. No DCB event data migration is required.

## Templates

Templates for 10.25.0 are released separately, after the libraries. Generated projects keep targeting net10.0.
