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
            Path.GetDirectoryName(typeof(ISekibanJsRuntime).Assembly.Location)!,
            "wwwroot", "sekiban-runtime.mjs"),

        // Blazor (WASM)
        PlatformID.Other => "./_content/Sekiban.Infrastructure.IndexedDb/sekiban-runtime.mjs",

        _ => throw new PlatformNotSupportedException(),
    };
}
