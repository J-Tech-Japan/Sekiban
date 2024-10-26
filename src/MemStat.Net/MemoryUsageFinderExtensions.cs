using Microsoft.Extensions.DependencyInjection;
using ResultBoxes;
using System.Runtime.InteropServices;
namespace MemStat.Net;

public static class MemoryUsageFinderExtensions
{
    public static IServiceCollection AddMemoryUsageFinder(this IServiceCollection services) => UnitValue.Unit switch
    {
        not null when RuntimeInformation.IsOSPlatform(OSPlatform.Linux) => services
            .AddTransient<IMemoryUsageFinder, LinuxMemoryUsageFinder>(),
        not null when RuntimeInformation.IsOSPlatform(OSPlatform.OSX) => services
            .AddTransient<IMemoryUsageFinder, MacMemoryUsageFinder>(),
        not null when RuntimeInformation.IsOSPlatform(OSPlatform.Windows) => services
            .AddTransient<IMemoryUsageFinder, WindowsMemoryUsageFinder>(),
        _ => throw new PlatformNotSupportedException()
    };
}
