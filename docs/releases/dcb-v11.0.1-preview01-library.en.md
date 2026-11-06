# Sekiban DCB 11.0.1-preview01

- Version: `11.0.1-preview01`. The first preview of the DCB 11 line. 26 DCB library packages.
- Targets **net10.0 and net11.0** (net9.0 is no longer targeted; stay on the 10.x line for net9.0). Built with C# 15 and the .NET 11 SDK `11.0.100-rc.3.26522.148`. Consuming the packages does not require the .NET 11 SDK on net10.0.
- The DCB library code and public API are the same as 10.24.0. No DCB event data migration is required.

## Dependencies

- **net10.0** uses the same dependency versions as DCB 10.24.0.
- **net11.0** uses the .NET 11 rc.3 packages: `Microsoft.Extensions.*` (those versioned with .NET), Entity Framework Core and `Microsoft.Data.Sqlite` `11.0.0-rc.3.26522.148`, and `Npgsql.EntityFrameworkCore.PostgreSQL` `11.0.0-rc.3`. `Npgsql` is 10.0.3 on net11.0.
- `Microsoft.Orleans.*` is 10.4.2 on both. For the Orleans 10.4.2 upgrade notes, see the 10.24.0 release notes.

## Preview status

- The net11.0 dependencies are release candidates. Later previews will move them to the .NET 11 GA packages. The stable 11.0.1 follows when all dependencies are stable.
- The 10.x line continues to receive releases while the 11 line is in preview.
- Templates are not released for this preview; the DCB templates stay on the 10.x line.
