# MemStat.Net

Get memory statistics of a process in .NET

## Usage

```csharp
builder.Services.AddMemoryUsageFinder();


app.MapGet(
    "/memoryusage",
    ([FromServices] IMemoryUsageFinder memoryUsageFinder) => memoryUsageFinder
        .ReceiveCurrentMemoryUsage()
        .Conveyor(_ => memoryUsageFinder.GetTotalMemoryUsage())
        .Combine(_ => memoryUsageFinder.GetMemoryUsagePercentage())
        .Remap((total, percent) => new MemoryInfo(total, percent))
        .Match(some => Results.Ok(some), error => Results.Ok(error.Message)));

internal record MemoryInfo(double TotalMemory, double MemoryUsagePercentage);

```

Before either `GetMemoryUsagePercentage()` or `GetTotalMemoryUsage()`, `ReceiveCurrentMemoryUsage()` must be
called.
`GetMemoryUsagePercentage()` returns 0 to 1 value. 1 means 100% memory usage. Sometime it may be more than 1.0.
`GetTotalMemoryUsage()` returns total memory usage in bytes.

## Installation

```bash
dotnet add package MemStat.Net
```
