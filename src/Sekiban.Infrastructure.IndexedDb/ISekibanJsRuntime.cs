using System.Reflection;
using Sekiban.Infrastructure.IndexedDb.Databases;

namespace Sekiban.Infrastructure.IndexedDb;

public interface ISekibanJsRuntime
{
    Task<ISekibanIndexedDbContext> CreateContextAsync(string context);

    public static string RuntimePath => Environment.OSVersion.Platform switch
    {
        // Windows, MacOS and Linux
        PlatformID.Win32NT or PlatformID.Unix => Path.Combine(
            Path.GetDirectoryName(Assembly.GetAssembly(typeof(ISekibanJsRuntime))!.Location)!,
            "wwwroot", "sekiban-runtime.mjs"),

        // Blazor (WASM)
        PlatformID.Other => "./sekiban-runtime.mjs",

        _ => throw new PlatformNotSupportedException(),
    };
}
