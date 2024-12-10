using System.Reflection;
using Sekiban.Infrastructure.IndexedDb.Databases;

namespace Sekiban.Infrastructure.IndexedDb;

public interface ISekibanJsRuntime
{
    Task<ISekibanIndexedDbContext> CreateContextAsync(string context);

    public static readonly string RuntimePath =
        Path.Combine(
            Path.GetDirectoryName(Assembly.GetAssembly(typeof(ISekibanJsRuntime))!.Location)!,
            "Assets", "sekiban-runtime.mjs"
        );
}
