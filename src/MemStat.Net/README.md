# MemStat.Net

Get memory statistics of a process in .NET

## Features

- Get total memory usage of a process
- Get memory usage percentage of a process

it works on Windows, Linux and MacOS.

- Windows : using `GlobalMemoryStatusEx` from `kernel32.dll`
    - Tested on Windows 11 and Azure App Service Windows Plan
- Linux : using `free` command and parse it.
    - Tested on Azure App Service Linux Plan
- MacOS : using `vm_stat` command and parse it.
    - Tested on macOS Sonoma

## Usage

```csharp
// you can add to DI container.
builder.Services.AddMemoryUsageFinder();

// Railway Oriented Programming way of using MemoryUsageFinder
app.MapGet(
    "/memoryusage",
    ([FromServices] IMemoryUsageFinder memoryUsageFinder) => memoryUsageFinder
        .ReceiveCurrentMemoryUsage()
        .Conveyor(_ => memoryUsageFinder.GetTotalMemoryUsage())
        .Combine(_ => memoryUsageFinder.GetMemoryUsagePercentage())
        .Remap((total, percent) => new MemoryInfo(total, percent))
        .Match(some => Results.Ok(some), error => Results.Ok(error.Message)));

internal record MemoryInfo(double TotalMemory, double MemoryUsagePercentage);


// Normal way of using MemoryUsageFinder
var memoryUsageFinder = serviceProvider.GetRequiredService<IMemoryUsageFinder>() ?? throw new InvalidOperationException("IMemoryUsageFinder is not registered");
memoryUsageFinder.ReceiveCurrentMemoryUsage();
var totalMemory = memoryUsageFinder.GetTotalMemoryUsage();
var memoryUsagePercentage = memoryUsageFinder.GetMemoryUsagePercentage();
// note : totalMemory and memoryUsagePercentage are in Result Type using ResultBox and double type.
if (totalMemory.IsSuccess)
{
   Console.WriteLine($"Total Memory : {totalMemory.GetValue()}");
}
if (memoryUsagePercentage.IsSuccess)
{
   Console.WriteLine($"Memory Usage Percentage : {memoryUsagePercentage.GetValue()}");
}

// you can also use it without DI
var memoryUsageFinder = new MemoryUsageFinder();
memoryUsageFinder.ReceiveCurrentMemoryUsage();
console.WriteLine(memoryUsageFinder.GetTotalMemoryUsage().UnwrapBox());
console.WriteLine(memoryUsageFinder.GetMemoryUsagePercentage().UnwrapBox());

// If you know you are using Windows, Linux or MacOS, you can use specific implementation.
var memoryUsageFinder = new WindowsMemoryUsageFinder();
// or var memoryUsageFinder = new LinuxMemoryUsageFinder();
// or var memoryUsageFinder = new MacOSMemoryUsageFinder();
console.WriteLine(memoryUsageFinder.GetTotalMemoryUsage().UnwrapBox());
console.WriteLine(memoryUsageFinder.GetMemoryUsagePercentage().UnwrapBox());
```

Before either `GetMemoryUsagePercentage()` or `GetTotalMemoryUsage()`, `ReceiveCurrentMemoryUsage()` must be
called.
`GetMemoryUsagePercentage()` returns 0 to 1 value. 1 means 100% memory usage. Sometime it may be more than 1.0.
`GetTotalMemoryUsage()` returns total memory usage in bytes.

## Installation

```bash
dotnet add package MemStat.Net
```
