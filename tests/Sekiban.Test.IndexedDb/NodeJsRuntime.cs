using System.Reflection;
using Microsoft.JavaScript.NodeApi;
using Microsoft.JavaScript.NodeApi.Runtime;
using Sekiban.Infrastructure.IndexedDb;
using Sekiban.Infrastructure.IndexedDb.Databases;

namespace Sekiban.Test.IndexedDb;

public class NodeJsRuntime : ISekibanJsRuntime
{
    private static readonly string basedir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
    private static readonly NodejsPlatform nodejsPlatform = new NodejsPlatform(Path.Combine(basedir, "Assets", "libnode.so.115"));
    private static readonly NodejsEnvironment nodejsEnvironment = nodejsPlatform.CreateEnvironment(basedir);

    public async Task<ISekibanIndexedDbContext> CreateContextAsync(string context)
    {
        var store = await nodejsEnvironment.RunAsync(async () =>
        {
            var store = await nodejsEnvironment.Import("./Assets/runtime.js", "init")
                .Call(JSValue.Undefined, context)
                .CastTo<JSPromise>()
                .AsTask();

            return new JSReference(store);
        });

        return new NodeIndexedDbContext(nodejsEnvironment, store);
    }
}
