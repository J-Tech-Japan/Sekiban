using Sekiban.Infrastructure.IndexedDb.Databases;

namespace Sekiban.Infrastructure.IndexedDb;

public interface ISekibanJsRuntime
{
    Task<ISekibanIndexedDbContext> CreateContextAsync(string context);
}
